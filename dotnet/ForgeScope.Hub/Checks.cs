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
        Check(AuthNavigation.ReturnTo("/repositories?lang=es&page=2", "en") == "/repositories?lang=es&page=2");
        foreach (string target in new[] { "https://evil.invalid/", "//evil.invalid/", "/auth/github", "/api/runner/claim", "/repositories\\evil", "/repositories#external", "/%2f%2fevil.invalid" })
            Check(AuthNavigation.ReturnTo(target, "pt-BR") == "/?lang=pt-BR");
        Console.WriteLine($"Hub: {checks} revision, blockers, runner binding, replay, stale results, path and locale checks passed"); return 0;
    }
}
