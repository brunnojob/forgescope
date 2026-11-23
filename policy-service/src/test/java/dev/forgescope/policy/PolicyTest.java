package dev.forgescope.policy;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.charset.StandardCharsets;
import java.util.List;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class PolicyTest {
    private static final ObjectMapper JSON = new ObjectMapper();

    @Test void loadsAllThreeRequirementsAndMatchesPaths() throws Exception {
        var policy = parse("""
            version: 1
            rules:
              - id: payments
                paths: ['payments/**']
                approvals: 1
                ci:
                  workflow: ci.yml
                  job: integration
                  artifact: integration-evidence
            """);
        var rule = policy.rules().getFirst();
        assertTrue(rule.applies(List.of("payments/api/Orders.java")));
        assertFalse(rule.applies(List.of("docs/README.md")));
        assertEquals(1, rule.approvals());
        assertEquals("integration", rule.ci().job());
        assertEquals("integration-evidence", rule.ci().artifact());
    }

    @Test void rejectsEmptyAndDuplicatePolicies() {
        assertThrows(Exception.class, () -> parse("version: 1\nrules: []"));
        assertThrows(Exception.class, () -> parse("version: 2\nversion: 1\nrules: []"));
    }

    @Test void ignoresAuthorStaleApprovalAndDismissedReview() throws Exception {
        var root = JSON.readTree("""
            [
              {"id":1,"user":{"id":3,"type":"User"},"author_association":"OWNER","state":"APPROVED","commit_id":"current"},
              {"id":2,"user":{"id":4,"type":"User"},"author_association":"MEMBER","state":"APPROVED","commit_id":"old"},
              {"id":3,"user":{"id":5,"type":"User"},"author_association":"COLLABORATOR","state":"APPROVED","commit_id":"current"},
              {"id":4,"user":{"id":6,"type":"User"},"author_association":"MEMBER","state":"APPROVED","commit_id":"current"},
              {"id":5,"user":{"id":6,"type":"User"},"author_association":"MEMBER","state":"DISMISSED","commit_id":"current"}
            ]
            """);
        assertEquals(1, PolicyService.approvals(java.util.stream.StreamSupport.stream(root.spliterator(), false).toList(), "current", 3));
    }

    private static Policy parse(String yaml) throws Exception {
        return Policy.read(yaml.getBytes(StandardCharsets.UTF_8));
    }
}
