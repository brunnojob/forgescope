package dev.forgescope.policy;

import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.dataformat.yaml.YAMLMapper;
import java.io.IOException;
import java.nio.file.FileSystems;
import java.nio.file.Path;
import java.util.List;
import java.util.HashSet;

record Policy(int version, List<Rule> rules) {
    private static final YAMLMapper YAML = YAMLMapper.builder()
        .enable(JsonParser.Feature.STRICT_DUPLICATE_DETECTION)
        .enable(com.fasterxml.jackson.databind.DeserializationFeature.FAIL_ON_TRAILING_TOKENS)
        .build();

    static Policy read(byte[] bytes) throws IOException {
        if (bytes.length > 32_768) throw new IllegalArgumentException("Policy exceeds 32 KiB");
        Policy policy = YAML.readValue(bytes, Policy.class);
        if (policy == null || policy.version != 1 || policy.rules == null || policy.rules.isEmpty() || policy.rules.size() > 30)
            throw new IllegalArgumentException("Expected version 1 and 1–30 rules");
        var ids = new HashSet<String>();
        for (Rule rule : policy.rules) {
            if (rule == null || rule.id == null || !rule.id.matches("[a-z0-9-]{1,50}") || !ids.add(rule.id)
                || rule.paths == null || rule.paths.isEmpty() || rule.paths.size() > 30
                || rule.paths.stream().anyMatch(p -> p == null || p.isBlank() || p.length() > 180))
                throw new IllegalArgumentException("Invalid id or paths");
            if (rule.approvals != null && (rule.approvals < 1 || rule.approvals > 10))
                throw new IllegalArgumentException("Invalid approval count");
            if (rule.ci != null) rule.ci.validate();
            if (rule.approvals == null && rule.ci == null) throw new IllegalArgumentException("Rule has no requirements");
            rule.paths.forEach(p -> FileSystems.getDefault().getPathMatcher("glob:" + p));
        }
        return policy;
    }

    record Rule(String id, List<String> paths, Integer approvals, Ci ci) {
        boolean applies(List<String> changed) {
            return paths.stream().map(p -> FileSystems.getDefault().getPathMatcher("glob:" + p))
                .anyMatch(matcher -> changed.stream().map(Path::of).anyMatch(matcher::matches));
        }
    }

    record Ci(String workflow, String job, String artifact) {
        void validate() {
            if (workflow == null || !workflow.matches("[A-Za-z0-9_.-]+\\.ya?ml")
                || (job == null && artifact == null) || invalid(job) || invalid(artifact))
                throw new IllegalArgumentException("Invalid workflow, job or artifact");
        }

        private static boolean invalid(String name) {
            return name != null && (name.isBlank() || name.length() > 120 || name.chars().anyMatch(Character::isISOControl));
        }
    }
}
