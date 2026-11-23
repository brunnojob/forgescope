package dev.forgescope.policy;

import com.fasterxml.jackson.databind.JsonNode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import dev.forgescope.Engines;
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
    private final AtomicBoolean requested = new AtomicBoolean();

    public PolicyService(JdbcTemplate db) { this.db = db; }
    public static void main(String[] args) throws Exception {
        if (args.length > 0 && args[0].equals("simulate")) {
            if (args.length != 3) throw new IllegalArgumentException("Usage: simulate owner/repo candidate.yml");
            simulate(args[1], Path.of(args[2]));
            return;
        }
        SpringApplication.run(PolicyService.class, args);
    }

    private static GitHub connect() throws Exception {
        return GitHub.installation(required("FORGESCOPE_APP_ID"),
            Long.parseLong(required("FORGESCOPE_INSTALLATION_ID")),
            Files.readString(Path.of(required("FORGESCOPE_PRIVATE_KEY_FILE"))));
    }

    private static void simulate(String repo, Path file) throws Exception {
        String path = "/repos/" + repository(repo);
        Policy candidate;
        try (var input = Files.newInputStream(file)) { candidate = Policy.read(input.readNBytes(32_769)); }
        var github = connect();
        var results = new ArrayList<Map<String, Object>>();
        for (JsonNode item : github.list(path + "/pulls?state=open", null)) {
            int number = item.required("number").asInt();
            String pull = path + "/pulls/" + number;
            JsonNode pr = github.get(pull);
            if (!pr.path("state").asText().equals("open")) continue;
            String head = sha(pr, "head");
            String base = sha(pr, "base");
            try {
                Evidence next = evaluatePolicy(github, path, pull, pr, head, base, candidate, false);
                var result = new LinkedHashMap<String, Object>();
                result.put("pull_request", number);
                result.put("head_sha", head);
                result.put("base_sha", base);
                result.put("candidate", next);
                try {
                    result.put("current", evaluatePolicy(github, path, pull, pr, head, base, loadPolicy(github, path, base), false));
                } catch (Exception unavailable) {
                    result.put("current_error", "Base policy or evidence unavailable");
                }
                JsonNode latest = github.get(pull);
                if (!head.equals(sha(latest, "head")) || !base.equals(sha(latest, "base")) || !latest.path("state").asText().equals("open"))
                    throw new IllegalStateException("Pull request changed");
                results.add(result);
            } catch (Exception unavailable) {
                results.add(Map.of("pull_request", number, "error", "Pull request changed or evidence unavailable; rerun preview"));
            }
        }
        System.out.println(new com.fasterxml.jackson.databind.ObjectMapper().writerWithDefaultPrettyPrinter().writeValueAsString(results));
    }

    void requestEvaluation() { requested.set(true); }

    @Scheduled(fixedDelay = 1000)
    public void processEvent() {
        if (requested.getAndSet(false)) reconcile();
    }

    static String repository(String repo) {
        if (!repo.matches("[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+") || repo.endsWith("/.") || repo.endsWith("/.."))
            throw new IllegalArgumentException("Invalid repository");
        return repo;
    }

    @Scheduled(fixedDelayString = "${forgescope.poll-ms:60000}")
    public void reconcile() {
        try {
            var github = connect();
            for (String configured : required("FORGESCOPE_REPOSITORIES").split(",")) {
                String repo = repository(configured.strip());
                try {
                    for (JsonNode pr : github.list("/repos/" + repo + "/pulls?state=open", null)) {
                        int number = pr.required("number").asInt();
                        try { evaluate(github, repo, number, required("FORGESCOPE_APP_ID")); }
                        catch (Exception error) { LOG.warn("Evaluation failed for {} #{} ({})", repo, number, error.getMessage()); }
                    }
                } catch (Exception error) { LOG.warn("Repository scan failed for {} ({})", repo, error.getMessage()); }
            }
        } catch (Exception error) {
            LOG.warn("GitHub App reconciliation unavailable ({})", error.getClass().getSimpleName());
        }
    }

    void evaluate(GitHub github, String repo, int number, String appId) throws Exception {
        String path = "/repos/" + repo;
        String pull = path + "/pulls/" + number;
        JsonNode pr = github.get(pull);
        if (!pr.path("state").asText().equals("open")) return;
        String head = sha(pr, "head");
        String base = sha(pr, "base");
        String externalId = repo + "#" + number;
        String checkPath = path + "/check-runs";
        var current = github.list(path + "/commits/" + head + "/check-runs?check_name=ForgeScope%20%2F%20merge%20policy&filter=all", "check_runs")
            .stream().filter(check -> check.path("app").path("id").asText().equals(appId)
                && check.path("external_id").asText().equals(externalId)).findFirst();

        Evidence evidence;
        try {
            evidence = evaluatePolicy(github, path, pull, pr, head, base, loadPolicy(github, path, base), true);
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

    private static Policy loadPolicy(GitHub github, String path, String base) throws Exception {
        JsonNode contents = github.get(path + "/contents/forgescope.yml?ref=" + base);
        if (!contents.path("type").asText().equals("file") || !contents.path("encoding").asText().equals("base64"))
            throw new IllegalArgumentException("Expected a regular policy file");
        return Policy.read(Base64.getMimeDecoder().decode(contents.required("content").asText()));
    }

    private static Evidence evaluatePolicy(GitHub github, String path, String pull, JsonNode pr,
                                           String head, String base, Policy policy, boolean diagnostics) throws Exception {
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
        var runs = new LinkedHashMap<String, Optional<JsonNode>>();
        boolean passed = true;
        for (Policy.Rule rule : rules) {
            var matching = files.stream().distinct().sorted().filter(file -> rule.applies(List.of(file))).toList();
            lines.add("### " + rule.id());
            lines.add("Changed paths: " + matching.stream().limit(3).map(PolicyService::literal)
                .collect(java.util.stream.Collectors.joining(", ")) + (matching.size() > 3 ? " (+" + (matching.size() - 3) + " more)" : ""));
            if (rule.approvals() != null) {
                lines.add("- Approvals: " + approvals + "/" + rule.approvals()
                    + (approvals < rule.approvals() ? ". Request an eligible reviewer for this commit." : ". Passed."));
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
                lines.add("- " + ci.summary());
                passed &= ci.passed();
            }
        }
        if (diagnostics) {
            String patchNotes = patchNotes(changes);
            if (!patchNotes.isBlank()) lines.add(patchNotes);
            for (var run : runs.values()) {
                if (run.isPresent() && run.get().path("conclusion").asText().equals("failure")) {
                    String ciNotes = ciNotes(github, path, run.get());
                    if (!ciNotes.isBlank()) lines.add(ciNotes);
                    break;
                }
            }
        }
        return new Evidence(passed, lines.isEmpty() ? "No rules matched the changed files." : String.join("\n\n", lines));
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
        if (selected.isEmpty()) return new Evidence(false, "Run " + literal(requirement.workflow()) + " on the current PR revision.");
        JsonNode run = selected.get();
        long id = run.required("id").asLong();
        int attempt = run.required("run_attempt").asInt();
        String url = "https://github.com" + path.substring("/repos".length()) + "/actions/runs/" + id;
        String link = "[CI run, attempt " + attempt + "](" + url + "/attempts/" + attempt + ")";
        if (!run.path("conclusion").asText().equals("success")) return new Evidence(false, link + " has not succeeded.");
        String endpoint = path + "/actions/runs/" + id;
        if (requirement.job() != null && github.list(endpoint + "/attempts/" + attempt + "/jobs", "jobs").stream()
            .noneMatch(job -> job.path("name").asText().equals(requirement.job()) && job.path("conclusion").asText().equals("success")))
            return new Evidence(false, link + " is missing successful job " + literal(requirement.job()) + ". Fix or enable the job and rerun CI.");
        if (requirement.artifact() != null) {
            Instant started = Instant.parse(github.get(endpoint + "/attempts/" + attempt).required("run_started_at").asText());
            if (github.list(endpoint + "/artifacts", "artifacts").stream().noneMatch(artifact ->
                artifact.path("name").asText().equals(requirement.artifact()) && !artifact.path("expired").asBoolean(true)
                    && !Instant.parse(artifact.required("created_at").asText()).isBefore(started)))
                return new Evidence(false, link + " is missing unexpired artifact " + literal(requirement.artifact()) + ". Upload it in this workflow attempt.");
        }
        return new Evidence(true, link + " meets the CI requirements.");
    }

    static String literal(String value) {
        String safe = value.replaceAll("[^\\p{L}\\p{N} ./_@:+-]", "?");
        return "`" + safe.substring(0, Math.min(120, safe.length())).strip() + "`";
    }

    private static String patchNotes(List<JsonNode> changes) {
        try {
            var diff = new StringBuilder();
            for (var change : changes) {
                String filename = change.path("filename").asText();
                String patch = change.path("patch").asText();
                if (patch.isBlank() || !filename.matches("[A-Za-z0-9._ /-]+")) continue;
                if (diff.length() + patch.length() > 250_000) break;
                diff.append("diff --git a/").append(filename).append(" b/").append(filename)
                    .append("\n--- a/").append(filename).append("\n+++ b/").append(filename).append('\n').append(patch).append('\n');
            }
            return notes("PatchScope", Engines.diff(diff.toString()).findings());
        } catch (Exception ignored) { return ""; }
    }

    private static String ciNotes(GitHub github, String path, JsonNode run) {
        try {
            var jobs = github.list(path + "/actions/runs/" + run.required("id").asLong()
                + "/attempts/" + run.required("run_attempt").asInt() + "/jobs", "jobs");
            var logs = new ArrayList<Map<String, Object>>();
            for (var job : jobs.stream().filter(j -> j.path("conclusion").asText().equals("failure")).limit(3).toList()) {
                logs.add(Map.of("name", "GitHub Actions job", "text", github.jobLog(path, job.required("id").asLong())));
            }
            return notes("CIFuse", Engines.logs(logs, Map.of()).stream()
                .filter(row -> row instanceof Map<?, ?> item && "cifuse".equals(item.get("engine"))).toList());
        } catch (Exception ignored) { return ""; }
    }

    private static String notes(String engine, List<Object> findings) {
        var items = findings.stream().filter(Map.class::isInstance).map(Map.class::cast).limit(5)
            .map(item -> "- " + literal(String.valueOf(item.get("title"))) +
                (item.get("path") instanceof String path ? " · " + literal(path) : "") +
                (item.get("count") instanceof Number count ? " · " + count + " occurrence(s)" : ""))
            .toList();
        return items.isEmpty() ? "" : "### " + engine + " · advisory\n" + String.join("\n", items);
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
