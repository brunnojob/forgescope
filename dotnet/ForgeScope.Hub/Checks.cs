using System.Text.Json;

namespace ForgeScope.Hub;

public static class Checks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value) { if (!value) throw new Exception("Hub regression failed at check " + checks); checks++; }
        void Denied(Action action) { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new Exception("Expected review rejection"); }
        using var report = JsonDocument.Parse("{\"schema_version\":1,\"findings\":[]}");
        var value = new Investigation { OwnerId = "42", Repository = "owner/repo", Commit = new('a', 40), Title = "<script>bad</script>", Report = report.RootElement.Clone() };
        Check(value.Stage == "investigation"); Check(JsonSerializer.Serialize(value).Length > 0);
        value.Revise("Fix import\nVerify tests", "patch", "42"); var digest = value.Current.Digest;
        value.Comment(digest, "Missing rollback", true, 2, "42"); Denied(() => value.Decide(digest, "approved", "42"));
        value.Resolve(digest, value.Current.Comments[0].Id, "42"); value.Decide(digest, "approved", "42"); value.Queue(digest, "42");
        Denied(() => value.Queue(digest, "42")); var claim = value.Claim()!.Value; Check(value.Claim() is null);
        var success = new ValidationResult(digest, Investigation.Hash("patch"), value.Commit, new(1, false), new(0, false));
        Denied(() => value.Complete(claim.Job.Id, "bad", success));
        Denied(() => value.Complete(claim.Job.Id, claim.Lease, success with { Commit = new('b', 40) }));
        value.Complete(claim.Job.Id, claim.Lease, success); Check(value.Stage == "validated");
        Denied(() => value.Complete(claim.Job.Id, claim.Lease, success));
        value.Revise("Different plan", "patch", "42"); Check(value.Stage == "review" && value.Current.Comments.Count == 0);
        Denied(() => value.Decide(digest, "approved", "42")); Check(value.Revisions[0].Decision == "approved");
        value.Decide(value.Current.Digest, "approved", "42"); value.Queue(value.Current.Digest, "42"); var second = value.Claim()!.Value;
        value.Revise("Third plan", "patch", "42"); value.Complete(second.Job.Id, second.Lease, success with { Digest = second.Job.Digest });
        Check(second.Job.State == "superseded" && value.Stage == "review");
        value.Decide(value.Current.Digest, "approved", "42"); value.Queue(value.Current.Digest, "42"); var third = value.Claim()!.Value;
        value.Complete(third.Job.Id, third.Lease, success with { Digest = third.Job.Digest, Before = new(0, false) }); Check(third.Job.State == "failed");
        Check(!GitHub.SafePath("../escape") && !GitHub.SafePath(".git/config") && !GitHub.SafePath("a/.env.production") && !GitHub.SafePath("C:/escape") && GitHub.SafePath("src/Hello.java"));
        Check(Runner.RemoteMatches("git@github.com:owner/repo.git", "owner/repo") && !Runner.RemoteMatches("https://evil.example/owner/repo", "owner/repo"));
        Check(GitHub.LogHost(new("https://fixture.blob.core.windows.net/log")) && !GitHub.LogHost(new("https://fixture.blob.core.windows.net.evil.invalid/log")) && !GitHub.LogHost(new("http://fixture.blob.core.windows.net/log")));
        foreach (var lang in new[] { "en", "pt-BR", "es" })
        {
            var ctx = new DefaultHttpContext(); ctx.Request.QueryString = new QueryString("?lang=" + lang);
            var html = Views.Detail(ctx, "csrf", value); Check(html.Contains("lang=\"" + lang + "\"", StringComparison.Ordinal) && !html.Contains("<script>bad</script>", StringComparison.Ordinal));
        }
        foreach (var diagnostic in new[] { "src/main.ts:12:3: error TS2304: Cannot find name x", "main.go:7:2: undefined: x", "src/main.c:9:2: error: undeclared x", "main.rb:4: syntax error", "File \"app.py\", line 8\n    missing()\nNameError: name missing is not defined", "error[E0425]: cannot find value\n --> src/main.rs:3:4", "src/Main.cs(3,9): error CS0103: missing name" })
            Check(AlertEvidence.LogDiagnostics(diagnostic).Count() == 1);
        Check(AlertEvidence.LogDiagnostics("upload report.json:5: completed").Count() == 0);
        Check(AlertEvidence.LogDiagnostics("../escape.go:4: undefined x").Count() == 0);
        Check(AlertEvidence.Clean("token=secret-value ghp_exampletoken1234567890").Contains("[redacted]", StringComparison.Ordinal));
        foreach (string language in new[] { "en", "pt-BR" })
        foreach (string theme in new[] { "black", "dark", "light" })
        {
            var ctx = new DefaultHttpContext(); ctx.Request.QueryString = new QueryString($"?lang={language}&theme={theme}&q=error&filter=unread");
            Check(Views.ErrorPage(ctx, 503).Contains($"body class=\"{theme}\"", StringComparison.Ordinal));
            Check(Views.Theme(ctx) == theme && Views.SettingsUrl(ctx, language == "en" ? "pt-BR" : "en", theme).Contains("q=error&filter=unread", StringComparison.Ordinal));
        }
        Console.WriteLine($"Hub: {checks} revision, blockers, runner binding, replay, stale results, path and locale checks passed"); return 0;
    }
}
