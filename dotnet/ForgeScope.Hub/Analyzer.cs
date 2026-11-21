using System.Diagnostics;
using System.Text.Json;

namespace ForgeScope.Hub;

public sealed class Analyzer(IConfiguration config)
{
    // One scan per process; the hosting platform can scale separate instances if needed.
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<JsonElement> Scan(string root, string diff, string log, string temp, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("Analyzer is busy; retry shortly");
        try
        {
            string jar = config["FORGESCOPE_JAR"] ?? throw new InvalidOperationException("Java analyzer is not configured");
            string worker = config["FORGESCOPE_WORKER"] ?? throw new InvalidOperationException("Roslyn worker is not configured");
            var output = Path.Combine(temp, "report"); var patchPath = Path.Combine(temp, "change.diff"); var logPath = Path.Combine(temp, "build.log");
            await File.WriteAllTextAsync(patchPath, diff, ct); await File.WriteAllTextAsync(logPath, log, ct);
            var result = await Execute("java", ["-jar", jar, root, "--diff", patchPath, "--log", logPath, "--csharp-worker", worker, "--output", output, "--fail-on", "engine-error"], 120, ct);
            if (result != 0) throw new InvalidOperationException("Analyzer did not complete; no successful report was recorded");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "report.json"), ct)); return report.RootElement.Clone();
        }
        finally { gate.Release(); }
    }
    public static async Task<int> Execute(string executable, IEnumerable<string> argv, int timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in argv) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start analyzer");
        // Drain continuously without retaining potentially sensitive/unbounded process output.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        try { await process.WaitForExitAsync(deadline.Token); await Task.WhenAll(stdout, stderr); return process.ExitCode; }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
            throw new InvalidOperationException("Analyzer timed out or was cancelled");
        }
    }
}
