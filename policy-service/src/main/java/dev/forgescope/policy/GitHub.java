package dev.forgescope.policy;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.net.URI;
import java.net.http.*;
import java.time.Duration;
import java.time.Instant;
import java.util.*;
import java.security.*;
import java.security.spec.PKCS8EncodedKeySpec;
import java.io.ByteArrayOutputStream;

final class GitHub {
    private final ObjectMapper json = new ObjectMapper();
    private final HttpClient client = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build();
    private final String token;

    GitHub(String token) { this.token = token; }

    static GitHub installation(String app, long installation, String pem) throws Exception {
        return app(app, pem).asInstallation(installation);
    }

    static GitHub app(String app, String pem) throws Exception {
        if (!app.matches("[0-9]+")) throw new IllegalArgumentException("Invalid App ID");
        var key = KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(privateKeyDer(pem)));
        var encoder = Base64.getUrlEncoder().withoutPadding();
        long now = Instant.now().getEpochSecond();
        var json = new ObjectMapper();
        String claims = encoder.encodeToString(json.writeValueAsBytes(Map.of("alg", "RS256", "typ", "JWT")))
            + "." + encoder.encodeToString(json.writeValueAsBytes(Map.of("iat", now - 30, "exp", now + 540, "iss", app)));
        Signature signature = Signature.getInstance("SHA256withRSA");
        signature.initSign(key);
        signature.update(claims.getBytes(java.nio.charset.StandardCharsets.US_ASCII));
        String jwt = claims + "." + encoder.encodeToString(signature.sign());
        return new GitHub(jwt);
    }

    static byte[] privateKeyDer(String pem) {
        boolean pkcs1 = pem.contains("-----BEGIN RSA PRIVATE KEY-----");
        String encoded = pem.replace("-----BEGIN PRIVATE KEY-----", "")
            .replace("-----END PRIVATE KEY-----", "")
            .replace("-----BEGIN RSA PRIVATE KEY-----", "")
            .replace("-----END RSA PRIVATE KEY-----", "")
            .replaceAll("\\s", "");
        byte[] key = Base64.getDecoder().decode(encoded);
        if (!pkcs1) return key;

        byte[] version = der(0x02, new byte[] {0});
        byte[] rsaAlgorithm = der(0x30, concat(
            new byte[] {0x06, 0x09, 0x2a, (byte) 0x86, 0x48, (byte) 0x86, (byte) 0xf7, 0x0d, 0x01, 0x01, 0x01},
            new byte[] {0x05, 0x00}));
        return der(0x30, concat(version, rsaAlgorithm, der(0x04, key)));
    }

    private static byte[] der(int tag, byte[] value) {
        var output = new ByteArrayOutputStream(value.length + 6);
        output.write(tag);
        if (value.length < 128) {
            output.write(value.length);
        } else {
            int bytes = 4 - Integer.numberOfLeadingZeros(value.length) / 8;
            output.write(0x80 | bytes);
            for (int shift = (bytes - 1) * 8; shift >= 0; shift -= 8) output.write(value.length >>> shift);
        }
        output.writeBytes(value);
        return output.toByteArray();
    }

    private static byte[] concat(byte[]... values) {
        var output = new ByteArrayOutputStream();
        for (byte[] value : values) output.writeBytes(value);
        return output.toByteArray();
    }

    GitHub asInstallation(long installation) throws Exception {
        if (installation < 1) throw new IllegalArgumentException("Invalid installation ID");
        JsonNode result = request("POST", "/app/installations/" + installation + "/access_tokens", Map.of());
        return new GitHub(result.required("token").asText());
    }

    JsonNode get(String path) throws Exception { return request("GET", path, null); }

    JsonNode request(String method, String path, Object body) throws Exception {
        var request = HttpRequest.newBuilder(URI.create("https://api.github.com" + path))
            .timeout(Duration.ofSeconds(30))
            .header("Authorization", "Bearer " + token)
            .header("Accept", "application/vnd.github+json")
            .header("X-GitHub-Api-Version", "2022-11-28")
            .header("Content-Type", "application/json")
            .method(method, body == null ? HttpRequest.BodyPublishers.noBody()
                : HttpRequest.BodyPublishers.ofByteArray(json.writeValueAsBytes(body))).build();
        var response = client.send(request, HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() < 200 || response.statusCode() >= 300)
            throw new java.io.IOException("GitHub API returned HTTP " + response.statusCode());
        return json.readTree(response.body());
    }

    String jobLog(String repoPath, long job) throws Exception {
        var endpoint = HttpRequest.newBuilder(URI.create("https://api.github.com" + repoPath + "/actions/jobs/" + job + "/logs"))
            .timeout(Duration.ofSeconds(15)).header("Authorization", "Bearer " + token).build();
        var redirect = client.send(endpoint, HttpResponse.BodyHandlers.discarding());
        if (redirect.statusCode() != 302) throw new java.io.IOException("GitHub job log unavailable");
        URI location = URI.create(redirect.headers().firstValue("Location").orElseThrow());
        String host = location.getHost();
        if (!"https".equals(location.getScheme()) || host == null || location.getUserInfo() != null
            || (location.getPort() != -1 && location.getPort() != 443)
            || !host.toLowerCase(Locale.ROOT).matches("[a-z0-9-]+\\.blob\\.core\\.windows\\.net"))
            throw new java.io.IOException("Unsupported GitHub log storage host");
        var download = HttpRequest.newBuilder(location).timeout(Duration.ofSeconds(15)).build();
        var response = client.send(download, HttpResponse.BodyHandlers.ofInputStream());
        try (var body = response.body()) {
            if (response.statusCode() != 200) throw new java.io.IOException("Job log download unavailable");
            return new String(body.readNBytes(120_000), java.nio.charset.StandardCharsets.UTF_8);
        }
    }

    List<JsonNode> list(String path, String key) throws Exception {
        List<JsonNode> items = new ArrayList<>();
        for (int page = 1; page <= 30; page++) {
            JsonNode body = get(path + (path.contains("?") ? "&" : "?") + "per_page=100&page=" + page);
            JsonNode array = key == null ? body : body.required(key);
            if (!array.isArray()) throw new IllegalArgumentException("Expected paginated GitHub array");
            array.forEach(items::add);
            if (array.size() < 100) return items;
        }
        throw new IllegalArgumentException("GitHub result exceeds supported pagination");
    }
}
