using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace ForgeScope.Hub;

public sealed record RunnerClaim(Guid Investigation, Guid Job, string Repository, string Commit, string Digest, string PatchDigest, string Patch, string Lease);
public sealed record RunnerCompletion(Guid Job, string Lease, ValidationResult Result);

public static class Runner
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !new[] { "--runner", "--repository", "--checkout", "--jar", "--test-command" }.Contains(args[i])) throw new InvalidOperationException("Invalid runner options");
                options.Add(args[i], args[i + 1]);
            }
            var url = new Uri(options["--runner"]);
            if (url.AbsolutePath != "/" || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 || url.Scheme != "https" && !(url.IsLoopback && url.Scheme == "http"))
                throw new InvalidOperationException("Runner requires an HTTPS hub origin (HTTP loopback allowed)");
            string repository = Investigation.RepositoryName(options["--repository"]), checkout = Path.GetFullPath(options["--checkout"]), jar = Path.GetFullPath(options["--jar"]);
            string command = options["--test-command"];
            var argv = JsonSerializer.Deserialize<string[]>(command);
            if (argv is null || argv.Length == 0 || argv.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Explicit test argv is required");
            string token = Environment.GetEnvironmentVariable("FORGESCOPE_RUNNER_TOKEN") ?? throw new InvalidOperationException("Set FORGESCOPE_RUNNER_TOKEN in the local process environment");
            var remote = await Git(checkout, "remote", "get-url", "origin");
            if (!RemoteMatches(remote.Trim(), repository)) throw new InvalidOperationException("Checkout origin does not match --repository");
            if (!string.IsNullOrWhiteSpace(await Git(checkout, "status", "--porcelain"))) throw new InvalidOperationException("Checkout must be clean and committed");
            var head = (await Git(checkout, "rev-parse", "HEAD")).Trim();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = url, Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var claimed = await http.PostAsync("/api/runner/claim", new StringContent("{}", Encoding.UTF8, "application/json"));
            if (claimed.StatusCode == HttpStatusCode.NoContent) { Console.WriteLine("No queued validation"); return 0; }
            claimed.EnsureSuccessStatusCode();
            var job = await claimed.Content.ReadFromJsonAsync<RunnerClaim>() ?? throw new InvalidOperationException("Missing validation job");
            var before = new TestExit(null, true); var after = new TestExit(null, true);
            int exit = 1;
            try
            {
                if (!string.Equals(job.Repository, repository, StringComparison.OrdinalIgnoreCase) || head != job.Commit || Investigation.Hash(job.Patch) != job.PatchDigest)
                    throw new InvalidOperationException("Job does not match local repository, commit or reviewed patch");
                string temp = Path.Combine(Path.GetTempPath(), "forgescope-runner-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
                try
                {
                    var patch = Path.Combine(temp, "reviewed.patch"); var output = Path.Combine(temp, "validation.json");
                    await File.WriteAllTextAsync(patch, job.Patch, new UTF8Encoding(false));
                    int result = await Analyzer.Execute("java", ["-jar", jar, "validate", checkout, patch, "--test-command", command, "--timeout", "120", "--output", output], 400, CancellationToken.None);
                    if (result is not (0 or 1) || !File.Exists(output)) throw new InvalidOperationException("Validation could not complete");
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
                    var first = document.RootElement.GetProperty("before"); var last = document.RootElement.GetProperty("after");
                    before = new(first.GetProperty("exit_code").ValueKind == JsonValueKind.Null ? null : first.GetProperty("exit_code").GetInt32(), first.GetProperty("timed_out").GetBoolean());
                    after = new(last.GetProperty("exit_code").ValueKind == JsonValueKind.Null ? null : last.GetProperty("exit_code").GetInt32(), last.GetProperty("timed_out").GetBoolean()); exit = result;
                }
                finally { Directory.Delete(temp, true); }
            }
            catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); }
            var completion = new RunnerCompletion(job.Job, job.Lease, new(job.Digest, job.PatchDigest, job.Commit, before, after));
            using var completed = await http.PostAsJsonAsync("/api/runner/complete", completion); completed.EnsureSuccessStatusCode();
            Console.WriteLine(exit == 0 ? "Validation reported: failing before, passing after" : "Validation failed or could not complete"); return exit;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or KeyNotFoundException or JsonException or UriFormatException)
        { Console.Error.WriteLine("Runner failed: " + ex.GetType().Name); return 2; }
    }
    public static bool RemoteMatches(string remote, string repository) => new[] { $"https://github.com/{repository}", $"https://github.com/{repository}.git", $"git@github.com:{repository}", $"git@github.com:{repository}.git", $"ssh://git@github.com/{repository}.git" }.Contains(remote, StringComparer.OrdinalIgnoreCase);
    static async Task<string> Git(string checkout, params string[] args)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-c", "core.fsmonitor=false", "-C", checkout }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new InvalidOperationException("Git operation timed out"); }
        await error; if (process.ExitCode != 0) throw new InvalidOperationException("Git operation failed"); return await output;
    }
}
