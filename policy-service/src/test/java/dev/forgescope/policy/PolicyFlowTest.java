package dev.forgescope.policy;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.jdbc.core.JdbcTemplate;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;

class PolicyFlowTest {
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final String REPOSITORY = "owner/demo-" + java.util.UUID.randomUUID();
    private static final String REPO = "/repos/" + REPOSITORY;
    private static final String BASE = "a".repeat(40);

    @Test
    void publishesFailureAndSuccessOnSeparateHeadCommits() throws Exception {
        var github = mock(GitHub.class);
        var db = auditDatabase();
        var service = new PolicyService(db);
        String bytes = Base64.getEncoder().encodeToString("""
            version: 1
            rules:
              - id: integration
                paths: ['demo/**']
                ci:
                  workflow: demo-ci.yml
                  job: integration
                  artifact: integration-evidence
            """.getBytes(StandardCharsets.UTF_8));
        when(github.get(REPO + "/contents/forgescope.yml?ref=" + BASE))
            .thenReturn(JSON.valueToTree(Map.of("type", "file", "encoding", "base64", "content", bytes)));
        when(github.list(REPO + "/pulls/1/files", null)).thenReturn(rows("""
            [{"filename":"demo/fixture.txt","patch":"@@ -0,0 +1 @@\n+example"}]
            """.replace("\n+example", "\\n+example")));
        when(github.list(contains("/check-runs?"), eq("check_runs"))).thenReturn(List.of());
        when(github.list(REPO + "/actions/runs/7/attempts/1/jobs", "jobs"))
            .thenReturn(rows("[{\"name\":\"integration\",\"conclusion\":\"success\"}]"));
        when(github.get(REPO + "/actions/runs/7/attempts/1"))
            .thenReturn(JSON.readTree("{\"run_started_at\":\"2026-01-01T00:00:00Z\"}"));

        String blocked = "b".repeat(40);
        stubRevision(github, blocked);
        when(github.list(REPO + "/actions/runs/7/artifacts", "artifacts")).thenReturn(List.of());
        service.evaluate(github, REPOSITORY, 1, "42");

        String passing = "c".repeat(40);
        stubRevision(github, passing);
        when(github.list(REPO + "/actions/runs/7/artifacts", "artifacts")).thenReturn(rows("""
            [{"name":"integration-evidence","expired":false,"created_at":"2026-01-01T00:01:00Z"}]
            """));
        service.evaluate(github, REPOSITORY, 1, "42");

        var payloads = ArgumentCaptor.forClass(Map.class);
        verify(github, times(2)).request(eq("POST"), eq(REPO + "/check-runs"), payloads.capture());
        var decisions = payloads.getAllValues();
        assertEquals(blocked, decisions.get(0).get("head_sha"));
        assertEquals("failure", decisions.get(0).get("conclusion"));
        assertTrue(decisions.get(0).get("output").toString().contains("is missing unexpired artifact"));
        assertTrue(decisions.get(0).get("output").toString().contains("demo/fixture.txt"));
        assertEquals(passing, decisions.get(1).get("head_sha"));
        assertEquals("success", decisions.get(1).get("conclusion"));
        if (System.getenv("FORGESCOPE_TEST_JDBC_URL") != null) {
            assertEquals(List.of("failure", "success"), db.queryForList(
                "SELECT conclusion FROM policy_evaluation WHERE repository = ? AND pull_request = 1 ORDER BY id",
                String.class, REPOSITORY));
            assertEquals(List.of(blocked, passing), db.queryForList(
                "SELECT head_sha FROM policy_evaluation WHERE repository = ? AND pull_request = 1 ORDER BY id",
                String.class, REPOSITORY));
        }
    }

    @Test
    void pollsRepositoriesUsingTheirOwnInstallationToken() throws Exception {
        var app = mock(GitHub.class);
        var first = mock(GitHub.class);
        var second = mock(GitHub.class);
        when(app.list("/app/installations", null)).thenReturn(rows("""
            [{"id":1},{"id":2},{"id":3,"suspended_at":"2026-01-01"}]
            """));
        when(app.asInstallation(1)).thenReturn(first);
        when(app.asInstallation(2)).thenReturn(second);
        when(first.list("/installation/repositories", "repositories")).thenReturn(rows("""
            [{"full_name":"alpha/one"},{"full_name":"alpha/archived","archived":true}]
            """));
        when(second.list("/installation/repositories", "repositories")).thenReturn(rows("""
            [{"full_name":"beta/two"}]
            """));
        new PolicyService(mock(JdbcTemplate.class)).reconcileInstallations(app, "42");
        verify(first).list("/repos/alpha/one/pulls?state=open", null);
        verify(second).list("/repos/beta/two/pulls?state=open", null);
        verify(app, never()).asInstallation(3);
        verifyNoMoreInteractions(first, second);
    }

    @Test
    void verifiesRawWebhookSignatureAndRejectsTampering() {
        String signature = "sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8";
        var original = "The quick brown fox jumps over the lazy dog".getBytes(StandardCharsets.UTF_8);
        assertTrue(GitHubEvents.valid("key", original, signature));
        original[0] = 'X';
        assertFalse(GitHubEvents.valid("key", original, signature));
        assertFalse(GitHubEvents.valid("key", original, "sha1=invalid"));
    }

    private static JdbcTemplate auditDatabase() {
        String url = System.getenv("FORGESCOPE_TEST_JDBC_URL");
        if (url == null) return mock(JdbcTemplate.class);
        var dataSource = new org.springframework.jdbc.datasource.DriverManagerDataSource(url,
            System.getenv("FORGESCOPE_TEST_JDBC_USER"), System.getenv("FORGESCOPE_TEST_JDBC_PASSWORD"));
        new org.springframework.jdbc.datasource.init.ResourceDatabasePopulator(
            new org.springframework.core.io.ClassPathResource("schema.sql")).execute(dataSource);
        return new JdbcTemplate(dataSource);
    }

    private static void stubRevision(GitHub github, String head) throws Exception {
        var headRef = Map.of("sha", head);
        var baseRef = Map.of("sha", BASE);
        var run = Map.of("id", 7, "run_attempt", 1, "conclusion", "success",
            "head_sha", head, "path", ".github/workflows/demo-ci.yml",
            "pull_requests", List.of(Map.of("number", 1, "head", headRef, "base", baseRef)));
        when(github.get(REPO + "/pulls/1")).thenReturn(JSON.valueToTree(Map.of(
            "state", "open", "number", 1, "head", headRef, "base", baseRef, "changed_files", 1)));
        when(github.list(REPO + "/actions/workflows/demo-ci.yml/runs?event=pull_request&head_sha=" + head, "workflow_runs"))
            .thenReturn(List.of(JSON.valueToTree(run)));
    }

    private static List<JsonNode> rows(String value) throws Exception {
        return java.util.stream.StreamSupport.stream(JSON.readTree(value).spliterator(), false).toList();
    }
}
