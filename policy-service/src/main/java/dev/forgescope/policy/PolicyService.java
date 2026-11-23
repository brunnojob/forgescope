package dev.forgescope.policy;

import com.fasterxml.jackson.databind.JsonNode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.*;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.scheduling.annotation.EnableScheduling;
import org.springframework.scheduling.annotation.Scheduled;

@SpringBootApplication
@EnableScheduling
public class PolicyService {
    static final String CHECK = "ForgeScope / merge policy";
    private static final Logger LOG = LoggerFactory.getLogger(PolicyService.class);
    private final JdbcTemplate db;

    public PolicyService(JdbcTemplate db) { this.db = db; }
    public static void main(String[] args) { SpringApplication.run(PolicyService.class, args); }

    @Scheduled(fixedDelayString = "${forgescope.poll-ms:60000}")
    public void reconcile() {
        try {
            var github = GitHub.installation(required("FORGESCOPE_APP_ID"),
                Long.parseLong(required("FORGESCOPE_INSTALLATION_ID")),
                Files.readString(Path.of(required("FORGESCOPE_PRIVATE_KEY_FILE"))));
            for (String configured : required("FORGESCOPE_REPOSITORIES").split(",")) {
                String repo = configured.strip();
                if (!repo.matches("[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+") || repo.endsWith("/.") || repo.endsWith("/.."))
                    throw new IllegalArgumentException("Invalid configured repository");
                try {
                    for (JsonNode pr : github.list("/repos/" + repo + "/pulls?state=open", null)) {
                        int number = pr.required("number").asInt();
                        try { evaluate(github, repo, number); }
                        catch (Exception error) { LOG.warn("Evaluation failed for {} #{} ({})", repo, number, error.getMessage()); }
                    }
                } catch (Exception error) { LOG.warn("Repository scan failed for {} ({})", repo, error.getMessage()); }
            }
        } catch (Exception error) {
            LOG.warn("GitHub App reconciliation unavailable ({})", error.getClass().getSimpleName());
        }
    }

    private void evaluate(GitHub github, String repo, int number) throws Exception {
        String path = "/repos/" + repo;
        String pull = path + "/pulls/" + number;
        JsonNode pr = github.get(pull);
        if (!pr.path("state").asText().equals("open")) return;
        String head = sha(pr, "head");
        String base = sha(pr, "base");
        String externalId = repo + "#" + number;
        String checkPath = path + "/check-runs";
        String appId = required("FORGESCOPE_APP_ID");
        var current = github.list(path + "/commits/" + head + "/check-runs?check_name=ForgeScope%20%2F%20merge%20policy&filter=all", "check_runs")
            .stream().filter(check -> check.path("app").path("id").asText().equals(appId)
                && check.path("external_id").asText().equals(externalId)).findFirst();

        Evidence evidence;
        try {
            evidence = evaluatePolicy(github, path, pull, pr, head, base);
        } catch (Exception error) {
            LOG.warn("Policy inputs unavailable for {} ({})", externalId, error.getMessage());
            evidence = new Evidence(false, "Policy input unavailable or invalid. Verify the base policy, App permissions and GitHub API.");
        }
        JsonNode latest = github.get(pull);
        if (!latest.path("state").asText().equals("open") || !head.equals(sha(latest, "head")) || !base.equals(sha(latest, "base"))) return;
        String conclusion = evidence.passed() ? "success" : "failure";
        String summary = "Base policy commit: `" + base + "`\n\nHead commit: `" + head + "`\n\n" + evidence.summary();
        if (current.isPresent() && current.get().path("conclusion").asText().equals(conclusion)
            && current.get().path("output").path("summary").asText().equals(summary)) return;

        db.update("""
            INSERT INTO policy_evaluation(repository, pull_request, head_sha, base_sha, conclusion, evidence)
            SELECT ?, ?, ?, ?, ?, ?
            WHERE NOT EXISTS (
                SELECT 1 FROM (SELECT head_sha, base_sha, conclusion, evidence FROM policy_evaluation
                WHERE repository = ? AND pull_request = ? ORDER BY id DESC LIMIT 1) AS latest
                WHERE head_sha = ? AND base_sha = ? AND conclusion = ? AND evidence = ?
            )
            """, repo, number, head, base, conclusion, summary, repo, number, head, base, conclusion, summary);
        Map<String, Object> payload = new HashMap<>(Map.of("name", CHECK, "status", "completed",
            "conclusion", conclusion, "output", Map.of("title", evidence.passed() ? "Policy passed" : "Policy failed", "summary", summary)));
        if (current.isPresent()) {
            github.request("PATCH", checkPath + "/" + current.get().required("id").asLong(), payload);
        } else {
            payload.put("external_id", externalId);
            payload.put("head_sha", head);
            github.request("POST", checkPath, payload);
        }
    }

    private static Evidence evaluatePolicy(GitHub github, String path, String pull, JsonNode pr, String head, String base) throws Exception {
        JsonNode contents = github.get(path + "/contents/forgescope.yml?ref=" + base);
        if (!contents.path("type").asText().equals("file") || !contents.path("encoding").asText().equals("base64"))
            throw new IllegalArgumentException("Expected a regular policy file");
        Policy policy = Policy.read(Base64.getMimeDecoder().decode(contents.required("content").asText()));
        List<JsonNode> changes = github.list(pull + "/files", null);
        if (changes.size() != pr.required("changed_files").asInt()) throw new IllegalArgumentException("Incomplete file list");
        List<String> files = new ArrayList<>();
        for (JsonNode change : changes) {
            files.add(change.required("filename").asText());
            if (change.hasNonNull("previous_filename")) files.add(change.get("previous_filename").asText());
        }
        var rules = policy.rules().stream().filter(rule -> rule.applies(files)).toList();
        int approvals = rules.stream().anyMatch(rule -> rule.approvals() != null)
            ? approvals(github.list(pull + "/reviews", null), head, pr.required("user").required("id").asLong()) : 0;
        var lines = new ArrayList<String>();
        var runs = new HashMap<String, Optional<JsonNode>>();
        boolean passed = true;
        for (Policy.Rule rule : rules) {
            if (rule.approvals() != null) {
                lines.add("- " + rule.id() + ": " + approvals + " eligible approval(s), " + rule.approvals() + " required.");
                passed &= approvals >= rule.approvals();
            }
            if (rule.ci() != null) {
                var requirement = rule.ci();
                String workflow = requirement.workflow();
                if (!runs.containsKey(workflow)) {
                    runs.put(workflow, github.list(path + "/actions/workflows/" + workflow
                        + "/runs?event=pull_request&head_sha=" + head, "workflow_runs").stream()
                        .filter(run -> matchesRun(run, pr, head, base, workflow))
                        .max(Comparator.comparingLong(run -> run.required("id").asLong())));
                }
                Evidence ci = checkCi(github, path, requirement, runs.get(workflow));
                lines.add("- " + rule.id() + ": " + ci.summary());
                passed &= ci.passed();
            }
        }
        return new Evidence(passed, lines.isEmpty() ? "No rules matched the changed files." : String.join("\n", lines));
    }

    private static boolean matchesRun(JsonNode run, JsonNode pr, String head, String base, String workflow) {
        if (!run.path("head_sha").asText().equals(head)
            || !run.path("path").asText().split("@", 2)[0].equals(".github/workflows/" + workflow)) return false;
        for (JsonNode candidate : run.path("pull_requests")) {
            if (candidate.path("number").asInt() == pr.path("number").asInt()
                && candidate.path("head").path("sha").asText().equals(head)
                && candidate.path("base").path("sha").asText().equals(base)) return true;
        }
        return false;
    }

    private static Evidence checkCi(GitHub github, String path, Policy.Ci requirement, Optional<JsonNode> selected) throws Exception {
        if (selected.isEmpty()) return new Evidence(false, "No matching workflow run for this PR and base revision.");
        JsonNode run = selected.get();
        long id = run.required("id").asLong();
        int attempt = run.required("run_attempt").asInt();
        String url = "https://github.com" + path.substring("/repos".length()) + "/actions/runs/" + id;
        String link = "[CI run, attempt " + attempt + "](" + url + "/attempts/" + attempt + ")";
        if (!run.path("conclusion").asText().equals("success")) return new Evidence(false, link + " has not succeeded.");
        String endpoint = path + "/actions/runs/" + id;
        if (requirement.job() != null && github.list(endpoint + "/attempts/" + attempt + "/jobs", "jobs").stream()
            .noneMatch(job -> job.path("name").asText().equals(requirement.job()) && job.path("conclusion").asText().equals("success")))
            return new Evidence(false, link + " has no successful required job.");
        if (requirement.artifact() != null) {
            Instant started = Instant.parse(github.get(endpoint + "/attempts/" + attempt).required("run_started_at").asText());
            if (github.list(endpoint + "/artifacts", "artifacts").stream().noneMatch(artifact ->
                artifact.path("name").asText().equals(requirement.artifact()) && !artifact.path("expired").asBoolean(true)
                    && !Instant.parse(artifact.required("created_at").asText()).isBefore(started)))
                return new Evidence(false, link + " has no required unexpired artifact from this attempt.");
        }
        return new Evidence(true, link + " meets the CI requirements.");
    }

    static int approvals(List<JsonNode> reviews, String head, long author) {
        var decisions = new HashMap<Long, JsonNode>();
        for (JsonNode review : reviews.stream().sorted(Comparator.comparingLong(r -> r.path("id").asLong())).toList()) {
            long reviewer = review.path("user").path("id").asLong();
            if (reviewer <= 0 || reviewer == author || !review.path("user").path("type").asText().equals("User")
                || !Set.of("OWNER", "MEMBER", "COLLABORATOR").contains(review.path("author_association").asText())) continue;
            if (Set.of("APPROVED", "CHANGES_REQUESTED", "DISMISSED").contains(review.path("state").asText()))
                decisions.put(reviewer, review);
        }
        return (int) decisions.values().stream().filter(r -> r.path("state").asText().equals("APPROVED")
            && r.path("commit_id").asText().equals(head)).count();
    }

    private static String sha(JsonNode pr, String ref) {
        String sha = pr.required(ref).required("sha").asText();
        if (!sha.matches("[0-9a-f]{40}")) throw new IllegalArgumentException("Invalid Git commit hash");
        return sha;
    }

    private static String required(String name) {
        String value = System.getenv(name);
        if (value == null || value.isBlank()) throw new IllegalArgumentException("Missing " + name);
        return value;
    }

    record Evidence(boolean passed, String summary) {}
}
