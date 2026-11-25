using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace ForgeScope.Hub;

public sealed class GitHub(HttpClient http)
{
    public async Task<string> Read(string path, string token, int limit, string accept = "application/vnd.github+json", CancellationToken ct = default)
    {
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid GitHub API path");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com" + path);
        request.Headers.UserAgent.ParseAdd("ForgeScope/0.4"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd(accept); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub returned HTTP {(int)response.StatusCode}; check access and rate limits");
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("GitHub input exceeds the supported limit");
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var data = new MemoryStream();
        var buffer = new byte[16_384]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (data.Length + read > limit) throw new InvalidOperationException("GitHub input exceeds the supported limit");
            data.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(data.ToArray());
    }
    public async Task<JsonElement> Json(string path, string token, CancellationToken ct = default)
    {
        using var document = JsonDocument.Parse(await Read(path, token, 8_000_000, ct: ct)); return document.RootElement.Clone();
    }
    public Task<JsonElement> Repositories(string token, int page, CancellationToken ct) => Json($"/user/repos?sort=updated&per_page=30&page={Math.Clamp(page, 1, 1000)}", token, ct);
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", "build", "dist", "target", ".venv", "vendor", ".forgescope" };
    public static bool SafePath(string path)
    {
        var parts = path.Split('/');
        return path.Length is > 0 and < 500 && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) &&
            !parts.Any(p => p is "" or "." or ".." || Excluded.Contains(p) || p.StartsWith(".env", StringComparison.OrdinalIgnoreCase));
    }
    public async Task<Investigation> Analyze(string owner, string token, string repository, int? pullRequest, string log, Analyzer analyzer, CancellationToken ct, long? runId = null)
    {
        repository = Investigation.RepositoryName(repository);
        var meta = await Json($"/repos/{repository}", token, ct);
        CiRun? ci = null;
        if (runId is not null)
        {
            if (runId < 1) throw new InvalidOperationException("Invalid workflow run id");
            var run = await Json($"/repos/{repository}/actions/runs/{runId}", token, ct);
            ci = new(runId.Value, run.GetProperty("head_sha").GetString()!, run.GetProperty("status").GetString()!, run.GetProperty("conclusion").ToString());
            log += "\n" + await Logs(repository, runId.Value, token, ct);
            if (Encoding.UTF8.GetByteCount(log) > 2_000_000) throw new InvalidOperationException("Combined CI logs exceed 2 MB");
        }
        string commit; string diff = "";
        if (pullRequest is not null)
        {
            if (pullRequest < 1) throw new InvalidOperationException("Invalid pull request number");
            var pr = await Json($"/repos/{repository}/pulls/{pullRequest}", token, ct);
            // Read source from the PR head repository, including fork PRs, without executing it.
            var head = pr.GetProperty("head");
            if (head.GetProperty("repo").ValueKind == JsonValueKind.Null) throw new InvalidOperationException("Pull request head repository no longer exists");
            var source = Investigation.RepositoryName(head.GetProperty("repo").GetProperty("full_name").GetString()!);
            commit = head.GetProperty("sha").GetString()!;
            diff = await Read($"/repos/{repository}/pulls/{pullRequest}", token, 2_000_000, "application/vnd.github.diff", ct);
            // Confirm that GitHub did not move the PR head while returning its current diff.
            var confirmed = await Json($"/repos/{repository}/pulls/{pullRequest}", token, ct);
            if (confirmed.GetProperty("head").GetProperty("sha").GetString() != commit) throw new InvalidOperationException("Pull request changed during import; retry");
            var result = await Snapshot(owner, token, repository, source, commit, pullRequest, diff, log, analyzer, ct); result.Ci = ci; return result;
        }
        var branch = Uri.EscapeDataString(meta.GetProperty("default_branch").GetString()!);
        var latest = await Json($"/repos/{repository}/commits/{branch}", token, ct); commit = latest.GetProperty("sha").GetString()!;
        var value = await Snapshot(owner, token, repository, repository, commit, null, diff, log, analyzer, ct); value.Ci = ci; return value;
    }
    public static bool LogHost(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        (uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".actions.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
    public async Task<string> JobLog(string repository, long job, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/actions/jobs/{job}/logs");
        request.Headers.UserAgent.ParseAdd("ForgeScope/0.4"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var redirect = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (redirect.StatusCode is not (System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.TemporaryRedirect) || redirect.Headers.Location is not { IsAbsoluteUri: true } location || !LogHost(location))
            throw new InvalidOperationException("Job logs are unavailable");
        using var download = new HttpRequestMessage(HttpMethod.Get, location);
        using var response = await http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 500_000) throw new InvalidOperationException("Job log exceeds 500 KB");
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream(); var buffer = new byte[16_384]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (bytes.Length + count > 500_000) throw new InvalidOperationException("Job log exceeds 500 KB"); bytes.Write(buffer, 0, count); }
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }
    async Task<string> Logs(string repository, long run, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/actions/runs/{run}/logs");
        request.Headers.UserAgent.ParseAdd("ForgeScope/0.4"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var redirect = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (redirect.StatusCode is not (System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.TemporaryRedirect) || redirect.Headers.Location is not { IsAbsoluteUri: true } location || !LogHost(location))
            throw new InvalidOperationException("GitHub logs are unavailable or use an unsupported download host");
        // Signed download URL only: never forward the GitHub bearer to another host or redirect.
        using var download = new HttpRequestMessage(HttpMethod.Get, location);
        using var response = await http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 10_000_000) throw new InvalidOperationException("CI archive unavailable or too large");
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream(); var buffer = new byte[16_384]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (bytes.Length + count > 10_000_000) throw new InvalidOperationException("CI archive too large"); bytes.Write(buffer, 0, count); }
        bytes.Position = 0; using var zip = new ZipArchive(bytes, ZipArchiveMode.Read); var output = new StringBuilder();
        var entries = zip.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length > 100 || entries.Sum(e => e.Length) > 2_000_000) throw new InvalidOperationException("CI log archive exceeds decoded limits");
        int total = 0;
        foreach (var entry in entries)
        {
            using var input = entry.Open(); using var decoded = new MemoryStream();
            while ((count = await input.ReadAsync(buffer, ct)) > 0) { total += count; if (total > 2_000_000) throw new InvalidOperationException("CI logs exceed decoded limits"); decoded.Write(buffer, 0, count); }
            output.AppendLine(new UTF8Encoding(false, true).GetString(decoded.ToArray()));
        }
        return output.ToString();
    }
    async Task<Investigation> Snapshot(string owner, string token, string repository, string source, string commit, int? pr, string diff, string log, Analyzer analyzer, CancellationToken ct)
    {
        if (!Regex.IsMatch(commit, "^[a-f0-9]{40}$")) throw new InvalidOperationException("Invalid commit identity");
        var tree = await Json($"/repos/{source}/git/trees/{commit}?recursive=1", token, ct);
        if (tree.GetProperty("truncated").GetBoolean()) throw new InvalidOperationException("Repository tree is truncated; use the local analyzer");
        var files = tree.GetProperty("tree").EnumerateArray().Where(f => f.GetProperty("type").GetString() == "blob" &&
            f.GetProperty("mode").GetString() is "100644" or "100755" && SafePath(f.GetProperty("path").GetString()!)).ToArray();
        // Do not call a partial download a clean audit. Larger repositories use the local workbench.
        if (files.Length > 250 || files.Sum(f => f.GetProperty("size").GetInt64()) > 10_000_000 || files.Any(f => f.GetProperty("size").GetInt64() > 512_000))
            throw new InvalidOperationException("Repository exceeds hosted scan limits; use the local analyzer and import its report");
        string temp = Path.Combine(Path.GetTempPath(), "forgescope-hub-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
        try
        {
            var root = Path.Combine(temp, "source"); Directory.CreateDirectory(root);
            foreach (var file in files)
            {
                var blob = await Json($"/repos/{source}/git/blobs/{file.GetProperty("sha").GetString()}", token, ct);
                if (blob.GetProperty("encoding").GetString() != "base64") throw new InvalidOperationException("Unsupported blob encoding");
                var bytes = Convert.FromBase64String(blob.GetProperty("content").GetString()!);
                if (bytes.Length != file.GetProperty("size").GetInt32()) throw new InvalidOperationException("Blob size mismatch");
                var path = Path.Combine(root, file.GetProperty("path").GetString()!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes, ct);
            }
            var report = await analyzer.Scan(root, diff, log, temp, ct);
            var value = new Investigation { OwnerId = owner, Repository = repository, Commit = commit, PullRequest = pr,
                Title = pr is null ? repository : $"{repository} #{pr}", Report = report };
            value.Event(owner, "analyzed", commit); return value;
        }
        finally { Directory.Delete(temp, true); }
    }
}
