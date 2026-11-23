package dev.forgescope.policy;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.HttpServer;
import jakarta.annotation.PostConstruct;
import jakarta.annotation.PreDestroy;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.HexFormat;
import java.util.Set;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import org.springframework.stereotype.Component;

@Component
final class GitHubEvents {
    private static final int MAX_BODY = 2_000_000;
    private static final Set<String> EVENTS = Set.of("pull_request", "pull_request_review", "workflow_run", "push");
    private final PolicyService policy;
    private final ObjectMapper mapper = new ObjectMapper();
    private HttpServer server;

    GitHubEvents(PolicyService policy) { this.policy = policy; }

    @PostConstruct
    void start() throws IOException {
        String secret = System.getenv("FORGESCOPE_WEBHOOK_SECRET");
        if (secret == null || secret.isBlank()) return;
        if (secret.length() < 32) throw new IllegalArgumentException("Webhook secret must be at least 32 characters");
        String address = System.getenv().getOrDefault("FORGESCOPE_WEBHOOK_BIND", "127.0.0.1");
        int port = Integer.parseInt(System.getenv().getOrDefault("FORGESCOPE_WEBHOOK_PORT", "8080"));
        String installation = System.getenv("FORGESCOPE_INSTALLATION_ID");
        var repositories = Arrays.stream(System.getenv().getOrDefault("FORGESCOPE_REPOSITORIES", "").split(","))
            .map(String::strip).toList();
        server = HttpServer.create(new InetSocketAddress(address, port), 16);
        server.createContext("/github/webhook", exchange -> {
            try (exchange) {
                if (!exchange.getRequestURI().getPath().equals("/github/webhook") || !exchange.getRequestMethod().equals("POST")) {
                    exchange.sendResponseHeaders(404, -1);
                    return;
                }
                var body = exchange.getRequestBody().readNBytes(MAX_BODY + 1);
                if (body.length > MAX_BODY) {
                    exchange.sendResponseHeaders(413, -1);
                    return;
                }
                if (!valid(secret, body, exchange.getRequestHeaders().getFirst("X-Hub-Signature-256"))) {
                    exchange.sendResponseHeaders(401, -1);
                    return;
                }
                String event = exchange.getRequestHeaders().getFirst("X-GitHub-Event");
                if (event != null && EVENTS.contains(event)) {
                    var payload = mapper.readTree(body);
                    if (payload == null || !payload.isObject()) {
                        exchange.sendResponseHeaders(400, -1);
                        return;
                    }
                    if (payload.path("installation").path("id").asText().equals(installation)
                        && repositories.stream().anyMatch(repo -> repo.equalsIgnoreCase(payload.path("repository").path("full_name").asText())))
                        policy.requestEvaluation();
                }
                exchange.sendResponseHeaders(202, -1);
            }
        });
        server.start();
    }

    @PreDestroy
    void stop() {
        if (server != null) server.stop(0);
    }

    static boolean valid(String secret, byte[] body, String signature) {
        if (signature == null || !signature.matches("sha256=[0-9a-f]{64}")) return false;
        try {
            var mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            return MessageDigest.isEqual(mac.doFinal(body), HexFormat.of().parseHex(signature.substring(7)));
        } catch (GeneralSecurityException impossible) {
            throw new IllegalStateException(impossible);
        }
    }
}
