using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace ForgeScope.Hub;

public sealed record RepoWatch(string Repository, DateTimeOffset? CheckedAt, string Error);
public sealed record Alert(string Key, string Repository, long Run, int Attempt, string Commit, string Workflow,
    string Job, string Step, string Message, string Path, int? Line, string EvidenceKind, DateTimeOffset CreatedAt)
{
    public bool Read { get; init; }
    public bool Saved { get; init; }
    public bool Done { get; init; }
    public string RunUrl => $"https://github.com/{Repository}/actions/runs/{Run}/attempts/{Attempt}";
    public string? SourceUrl => GitHub.SafePath(Path) && Regex.IsMatch(Commit, "^[a-f0-9]{40}$")
        ? $"https://github.com/{Repository}/blob/{Commit}/{string.Join('/', Path.Split('/').Select(Uri.EscapeDataString))}" + (Line > 0 ? $"#L{Line}" : "") : null;
}

public static class AlertEvidence
{
    public static string Clean(string text, int max = 6000)
    {
        text = Regex.Replace(text, "\\x1b\\[[0-9;]*[A-Za-z]", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"\b(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|AKIA[A-Z0-9]{16})\b", "[redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"(?i)\b(password|token|api[_-]?key|secret|authorization)\s*[:=]\s*[^\s,;]+", "$1=[redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        return text[..Math.Min(max, text.Length)];
    }
    // Compiler/linter formats are parsed as diagnostics, never promoted to a proven root cause.
    public static IEnumerable<(string Path, int Line, string Message)> LogDiagnostics(string log)
    {
        var lines = Clean(log, 500_000).Split('\n'); var seen = new HashSet<string>();
        string rustError = "";
        for (int index = 0; index < lines.Length; index++)
        {
            string raw = lines[index];
            string text = Regex.Replace(raw.Trim(), @"^\d{4}-\d\d-\d\dT\S+\s+", "", RegexOptions.None, TimeSpan.FromSeconds(1));
            var python = Regex.Match(text, "File \"(?<path>[^\"]+)\", line (?<line>[0-9]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (python.Success && GitHub.SafePath(python.Groups["path"].Value) && int.TryParse(python.Groups["line"].Value, out int pythonLine) && pythonLine > 0)
            {
                string? error = lines.Skip(index + 1).Take(6).Select(x => x.Trim()).FirstOrDefault(x => Regex.IsMatch(x, @"\b[A-Za-z]*(?:Error|Exception):", RegexOptions.None, TimeSpan.FromSeconds(1)));
                if (error is not null && seen.Add(python.Groups["path"].Value + ":" + pythonLine + ":" + error))
                { yield return (python.Groups["path"].Value, pythonLine, error); if (seen.Count >= 20) yield break; }
            }
            if (text.StartsWith("error[", StringComparison.Ordinal)) rustError = text;
            var m = Regex.Match(text, @"(?:^|\s)(?<path>[A-Za-z0-9_./-]+\.[A-Za-z0-9]+)(?::(?<line>[0-9]+)(?::[0-9]+)?|\((?<line>[0-9]+)(?:,[0-9]+)?\))\s*:?\s*(?<message>.*)$", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!m.Success || !int.TryParse(m.Groups["line"].Value, out int line) || line < 1) continue;
            string path = m.Groups["path"].Value; if (!GitHub.SafePath(path)) continue;
            string message = m.Groups["message"].Value.Trim();
            if (text.StartsWith("-->", StringComparison.Ordinal)) message = rustError;
            if (!Regex.IsMatch(message, @"(?i)error|undefined|cannot|failed|panic|fatal|exception|assert", RegexOptions.None, TimeSpan.FromSeconds(1)) || !seen.Add(path + ":" + line + ":" + message)) continue;
            yield return (path, line, message);
            if (seen.Count >= 20) yield break;
        }
    }
    public static Alert FromAnnotation(string repo, JsonElement run, JsonElement job, string step, JsonElement annotation)
    {
        string path = Views.Text(annotation, "path");
        int? line = annotation.TryGetProperty("start_line", out var n) && n.TryGetInt32(out int v) && v > 0 ? v : null;
        string message = Clean(Views.Text(annotation, "message"));
        if (string.IsNullOrWhiteSpace(message)) message = Clean(Views.Text(annotation, "title"));
        return Make(repo, run, job, step, message, path, line, "annotation");
    }
    public static Alert Make(string repo, JsonElement run, JsonElement job, string step, string message, string path = "", int? line = null, string kind = "step")
    {
        long id = run.GetProperty("id").GetInt64();
        int attempt = run.TryGetProperty("run_attempt", out var a) ? a.GetInt32() : 1;
        string sha = Views.Text(run, "head_sha");
        if (!Regex.IsMatch(sha, "^[a-f0-9]{40}$") || id < 1 || attempt < 1) throw new InvalidOperationException("Invalid workflow identity");
        string name = Clean(Views.Text(job, "name"), 300); message = Clean(message);
        string key = Investigation.Hash($"{repo.ToLowerInvariant()}:{id}:{attempt}:{name}:{step}:{path}:{line}:{kind}:{message}");
        return new(key, repo, id, attempt, sha, Clean(Views.Text(run, "name"), 300), name, Clean(step, 300), message,
            GitHub.SafePath(path) ? path : "", line, kind, DateTimeOffset.UtcNow);
    }
}

// Reads provider-supplied diagnostics, irrespective of source language. It never executes repository code.
public sealed class NotificationSync(Store store, GitHub github)
{
    public async Task Sync(string owner, string repository, string token, CancellationToken ct)
    {
        repository = Investigation.RepositoryName(repository);
        if (!await store.LeaseWatch(owner, repository)) return;
        try
        {
            var runs = await github.Json($"/repos/{repository}/actions/runs?status=completed&per_page=20", token, ct);
            foreach (var run in Views.Items(runs, "workflow_runs").Where(r => Views.Text(r, "conclusion") is "failure" or "timed_out" or "action_required").Take(5))
            {
                long id = run.GetProperty("id").GetInt64(); int attempt = run.TryGetProperty("run_attempt", out var a) ? a.GetInt32() : 1;
                if (await store.SeenRun(owner, repository, id, attempt)) continue;
                var jobs = await github.Json($"/repos/{repository}/actions/runs/{id}/attempts/{attempt}/jobs?per_page=100", token, ct);
                if (jobs.TryGetProperty("total_count", out var total) && total.GetInt32() > 100) throw new InvalidOperationException("Job list exceeds supported limits");
                var alerts = new List<Alert>();
                foreach (var job in Views.Items(jobs, "jobs").Where(j => Views.Text(j, "conclusion") is "failure" or "timed_out" or "action_required").Take(20))
                {
                    string step = string.Join(" · ", Views.Items(job, "steps").Where(s => Views.Text(s, "conclusion") is "failure" or "timed_out").Select(s => Views.Text(s, "name")));
                    string check = Views.Text(job, "check_run_url");
                    var match = Regex.Match(check, "^https://api\\.github\\.com/repos/" + Regex.Escape(repository) + "/check-runs/([0-9]+)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    bool found = false;
                    if (match.Success)
                    {
                        var annotations = await github.Json($"/repos/{repository}/check-runs/{match.Groups[1].Value}/annotations?per_page=100", token, ct);
                        if (annotations.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Invalid annotation response");
                        foreach (var annotation in annotations.EnumerateArray().Where(n => Views.Text(n, "annotation_level") == "failure"))
                        { alerts.Add(AlertEvidence.FromAnnotation(repository, run, job, step, annotation)); found = true; }
                    }
                    if (!found)
                    {
                        try
                        {
                            var log = await github.JobLog(repository, job.GetProperty("id").GetInt64(), token, ct);
                            foreach (var diagnostic in AlertEvidence.LogDiagnostics(log))
                            { alerts.Add(AlertEvidence.Make(repository, run, job, step, diagnostic.Message, diagnostic.Path, diagnostic.Line, "log")); found = true; }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { /* Preserve the known failed step when details are unavailable. */ }
                        if (!found) alerts.Add(AlertEvidence.Make(repository, run, job, step, "GitHub reported " + Views.Text(job, "conclusion") + ". " + step));
                    }
                }
                // An empty/partial job response must not be mistaken for a diagnosed or clean run.
                if (alerts.Count == 0) alerts.Add(AlertEvidence.Make(repository, run, default, "", "Workflow failed; no job diagnostic was available."));
                await store.SaveRunAlerts(owner, repository, id, attempt, alerts);
            }
            await store.WatchResult(owner, repository, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            await store.WatchResult(owner, repository, "GitHub synchronization unavailable; check access, rate limits and retry.");
            throw;
        }
    }
}

public sealed class NotificationPoller(IServiceProvider services, ILogger<NotificationPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var store = services.GetRequiredService<Store>();
                foreach (var (owner, repo, protectedToken) in await store.PollWatches())
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(45));
                        var token = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("github-token-v1").Unprotect(protectedToken);
                        await services.GetRequiredService<NotificationSync>().Sync(owner, repo, token, timeout.Token);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Notification sync deferred ({Type})", ex.GetType().Name); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Notification polling deferred ({Type}); apply schema migration before enabling monitoring", ex.GetType().Name); }
        }
    }
}
