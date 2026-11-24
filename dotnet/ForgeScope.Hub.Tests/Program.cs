using ForgeScope.Hub;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

if (args.Length != 2) throw new ArgumentException("Hub integration: JAVA_JAR WORKER_DLL; set FORGESCOPE_TEST_DATABASE to a disposable local PostgreSQL database");
string database = Environment.GetEnvironmentVariable("FORGESCOPE_TEST_DATABASE") ?? throw new ArgumentException("A disposable test database is required");
var dbOptions = new NpgsqlConnectionStringBuilder(database);
if (dbOptions.Host is not ("127.0.0.1" or "localhost")) throw new ArgumentException("Tests require local PostgreSQL");
var directory = Path.Combine(Path.GetTempPath(), "forgescope-hub-test-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
var checkout = Path.Combine(directory, "checkout"); Directory.CreateDirectory(checkout);
var jar = Path.GetFullPath(args[0]); var worker = Path.GetFullPath(args[1]);
async Task<string> Git(params string[] argv)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = checkout, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in argv) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception(await error); return await output;
}
await Git("init"); await Git("config", "user.name", "ForgeScope tests"); await Git("config", "user.email", "fixture@example.invalid");
await Git("remote", "add", "origin", "https://github.com/fixture/repo.git");
await File.WriteAllTextAsync(Path.Combine(checkout, "Test.java"), "class Test { public static void main(String[] args) { System.exit(1); } }\n");
await File.WriteAllTextAsync(Path.Combine(checkout, "README.md"), "# Fixture\n\n## Setup\nJDK 21\n## Usage\njava Test.java\n## Testing\njava Test.java\n");
await File.WriteAllTextAsync(Path.Combine(checkout, "LICENSE"), "Fixture license\n");
await Git("add", "."); await Git("commit", "-m", "fixture"); string commit = (await Git("rev-parse", "HEAD")).Trim();
var files = new Dictionary<string, byte[]>(); foreach (var name in new[] { "Test.java", "README.md", "LICENSE" }) files[name] = await File.ReadAllBytesAsync(Path.Combine(checkout, name));
await File.WriteAllTextAsync(Path.Combine(checkout, "Test.java"), "class Test { public static void main(String[] args) { System.exit(0); } }\n"); string patch = await Git("diff", "--no-ext-diff"); await Git("checkout", "--", "Test.java");
var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); int port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
string origin = $"http://127.0.0.1:{port}";
using var rsa = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=ForgeScope integration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
string keyCertificate = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
var fake = new FakeGitHub(commit, patch, files);
var app = ForgeScope.Hub.Program.CreateApp([], b =>
{
    b.Logging.ClearProviders();
    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["FORGESCOPE_PUBLIC_URL"] = origin, ["FORGESCOPE_DATABASE"] = database, ["FORGESCOPE_KEY_CERTIFICATE"] = keyCertificate,
        ["GITHUB_CLIENT_ID"] = "test-client", ["GITHUB_CLIENT_SECRET"] = "test-secret", ["FORGESCOPE_JAR"] = jar, ["FORGESCOPE_WORKER"] = worker });
}, fake);
app.Urls.Add(origin); int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("Hub HTTP check failed: " + label); checks++; }
HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromMinutes(5) };
async Task SignIn(HttpClient client, string account)
{
    using var begin = await client.GetAsync("/auth/github?lang=pt-BR"); Check(begin.StatusCode == HttpStatusCode.Redirect, "OAuth challenge");
    var query = QueryHelpers.ParseQuery(begin.Headers.Location!.Query); Check(query["code_challenge_method"] == "S256" && query["code_challenge"].ToString().Length == 43, "PKCE S256");
    Check(query["redirect_uri"] == origin + "/signin-github", "canonical callback");
    using var callback = await client.GetAsync("/signin-github?code=" + account + "&state=" + Uri.EscapeDataString(query["state"]!));
    Check(callback.StatusCode == HttpStatusCode.Redirect && callback.Headers.Location!.ToString() == "/?lang=pt-BR", "OAuth callback success");
    using var replay = await client.GetAsync("/signin-github?code=" + account + "&state=" + Uri.EscapeDataString(query["state"]!));
    Check(replay.Headers.Location?.ToString() == "/?auth=failed", "OAuth callback replay rejected");
}
async Task<(string Token, int Version, string Digest, string Html)> Form(HttpClient client, Guid id)
{
    string html = await client.GetStringAsync($"/investigations/{id}?lang=en");
    string Match(string name) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\" value=\"([^\"]*)\"").Groups[1].Value);
    return (Match("__RequestVerificationToken"), int.Parse(Match("version")), Match("digest"), html);
}
async Task<HttpResponseMessage> Post(HttpClient client, Guid id, string action, Dictionary<string, string> fields)
{
    var form = await Form(client, id); fields["__RequestVerificationToken"] = form.Token; fields["version"] = form.Version.ToString(); fields["digest"] = form.Digest;
    return await client.PostAsync($"/investigations/{id}/{action}", new FormUrlEncodedContent(fields));
}
try
{
    var store = app.Services.GetRequiredService<Store>(); await store.Migrate(); await store.Migrate(); Check(true, "idempotent isolated schema");
    await app.StartAsync(); using var anon = Client(); using var alice = Client(); using var bob = Client();
    using (var favicon = await anon.GetAsync("/favicon.ico"))
    {
        var bytes = await favicon.Content.ReadAsByteArrayAsync();
        Check(favicon.IsSuccessStatusCode && favicon.Content.Headers.ContentType?.MediaType == "image/x-icon" && bytes.Length > 100 && bytes[2] == 1, "ICO served without authentication");
    }
    using (var logo = await anon.GetAsync("/assets/branding/forgescope-github.png"))
    {
        var bytes = await logo.Content.ReadAsByteArrayAsync();
        Check(logo.IsSuccessStatusCode && logo.Content.Headers.ContentType?.MediaType == "image/png" && bytes.Length < 1_000_000 && bytes[0] == 137, "GitHub PNG served below 1 MB");
        Check(logo.Headers.GetValues("Content-Security-Policy").Single().Contains("img-src 'self'", StringComparison.Ordinal), "CSP permits only local brand images");
    }
    Check((await anon.GetAsync("/repositories")).StatusCode == HttpStatusCode.Unauthorized, "anonymous repository access rejected");
    Check((await anon.PostAsync("/api/runner/claim", new StringContent("{}"))).StatusCode == HttpStatusCode.Unauthorized, "runner requires bearer");
    using (var spoof = new HttpRequestMessage(HttpMethod.Get, "/auth/github")) { spoof.Headers.Host = "evil.invalid"; Check((await anon.SendAsync(spoof)).StatusCode == HttpStatusCode.BadRequest, "spoofed Host rejected"); }
    await SignIn(alice, "alice"); await SignIn(bob, "bob"); Check(fake.PkceExchanged, "verifier exchanged on server");
    using (var command = app.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT xml FROM forgescope.data_protection_keys LIMIT 1"))
    {
        string key = (string)(await command.ExecuteScalarAsync())!;
        Check(key.Contains("encryptedSecret", StringComparison.Ordinal) && !key.Contains("<masterKey", StringComparison.Ordinal), "PostgreSQL key ring is certificate encrypted");
    }
    var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test");
    string protectedValue = protector.Protect("fixture");
    var services = new ServiceCollection(); services.AddLogging();
    services.AddDataProtection().SetApplicationName("ForgeScope.Hub").ProtectKeysWithCertificate(certificate);
    services.Configure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(o => o.XmlRepository = new PostgresKeys(app.Services.GetRequiredService<NpgsqlDataSource>()));
    using (var provider = services.BuildServiceProvider()) Check(provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test").Unprotect(protectedValue) == "fixture", "persisted key ring works across independent server instances");
    string repos = await alice.GetStringAsync("/repositories"); Check(repos.Contains("fixture/repo", StringComparison.Ordinal), "authenticated repository list");
    string catalog = await alice.GetStringAsync("/catalog?lang=es&q=PCAPdroid");
    Check(catalog.Contains("PCAPdroid", StringComparison.Ordinal) && !catalog.Contains("<h2>Ghidra</h2>", StringComparison.Ordinal), "integrated RevScope catalog filter");
    string csrf = WebUtility.HtmlDecode(Regex.Match(repos, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    using (var bad = await alice.PostAsync("/investigations", new FormUrlEncodedContent(new Dictionary<string, string> { ["repository"] = "fixture/repo" }))) Check(bad.StatusCode == HttpStatusCode.Forbidden, "missing CSRF rejected");
    using var created = await alice.PostAsync("/investigations", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf, ["repository"] = "fixture/repo", ["pr"] = "1", ["run"] = "99", ["log"] = "Test.java:1: error: missing symbol", ["lang"] = "es" }));
    Check(created.StatusCode == HttpStatusCode.Redirect, "GitHub snapshot through real Java/Roslyn analyzer: " + await created.Content.ReadAsStringAsync());
    var id = Guid.Parse(created.Headers.Location!.ToString().Split('/')[2].Split('?')[0]);
    Check((await bob.GetAsync($"/investigations/{id}")).StatusCode == HttpStatusCode.NotFound, "cross-account isolation");
    Check((await bob.GetAsync($"/investigations/{id}/export")).StatusCode == HttpStatusCode.NotFound, "cross-account export isolation");
    var before = await Form(alice, id); Check(!before.Html.Contains("fake-token", StringComparison.Ordinal), "no provider token in HTML");
    Check(fake.LogDownloadWithoutToken && before.Html.Contains("different commit", StringComparison.Ordinal), "CI signed download strips bearer and mismatched SHA is disclosed");
    Check(before.Html.Contains("PatchScope", StringComparison.OrdinalIgnoreCase) && before.Html.Contains("PlanRelay", StringComparison.Ordinal), "analysis and review in one page");
    Check((await Post(alice, id, "revise", new() { ["plan"] = "Fix exit status\nRun regression", ["patch"] = patch })).StatusCode == HttpStatusCode.Redirect, "create plan revision");
    var stale = await Form(alice, id);
    Check((await Post(alice, id, "comment", new() { ["body"] = "<script>unsafe</script>", ["line"] = "1", ["blocker"] = "true" })).StatusCode == HttpStatusCode.Redirect, "anchored blocker");
    Check((await Post(alice, id, "decide", new() { ["decision"] = "approved" })).StatusCode == HttpStatusCode.Conflict, "approval blocked");
    var record = (await store.Get("101", id))!;
    Check((await Post(alice, id, "resolve", new() { ["comment"] = record.Current.Comments[0].Id.ToString() })).StatusCode == HttpStatusCode.Redirect, "resolve blocker");
    using var staleResponse = await alice.PostAsync($"/investigations/{id}/decide", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = stale.Token, ["version"] = stale.Version.ToString(), ["digest"] = stale.Digest, ["decision"] = "approved" }));
    Check(staleResponse.StatusCode == HttpStatusCode.Conflict, "stale browser form rejected");
    Check((await Post(alice, id, "decide", new() { ["decision"] = "approved" })).StatusCode == HttpStatusCode.Redirect, "approve exact revision");
    Check((await Post(alice, id, "queue", new())).StatusCode == HttpStatusCode.Redirect, "queue reviewed patch");
    using var tokenPage = await Post(alice, id, "runner-token", new()); string tokenHtml = await tokenPage.Content.ReadAsStringAsync();
    string runnerToken = Regex.Match(tokenHtml, "<pre>([a-f0-9]{64})</pre>").Groups[1].Value; Check(runnerToken.Length == 64, "scoped one-hour runner token");
    Environment.SetEnvironmentVariable("FORGESCOPE_RUNNER_TOKEN", runnerToken);
    int result = await Runner.Run(["--runner", origin, "--repository", "fixture/repo", "--checkout", checkout, "--jar", jar, "--test-command", "[\"java\",\"Test.java\"]"]);
    Check(result == 0, "real runner fail-before/pass-after");
    var completed = (await store.Get("101", id))!; Check(completed.Stage == "validated", "result persisted");
    Check((await Git("status", "--porcelain")).Trim() == "" && (await Git("rev-parse", "HEAD")).Trim() == commit, "original checkout preserved");
    using var denied = await bob.PostAsync($"/investigations/{id}/queue", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf, ["version"] = "1" })); Check(denied.StatusCode == HttpStatusCode.Forbidden, "cross-session CSRF rejected");
    foreach (var lang in new[] { "pt-BR", "es", "en" }) { var html = await alice.GetStringAsync($"/investigations/{id}?lang={lang}"); Check(html.Contains("lang=\"" + lang + "\"", StringComparison.Ordinal) && !html.Contains("<script>unsafe</script>", StringComparison.Ordinal), "locale and escaped review content"); }
    await Post(alice, id, "revise", new() { ["plan"] = "Changed scope", ["patch"] = patch }); Check((await store.Get("101", id))!.Stage == "review", "new revision invalidates readiness");
    var latest = await Form(alice, id);
    using var logout = await alice.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = latest.Token })); Check(logout.StatusCode == HttpStatusCode.Redirect, "logout");
    Check((await alice.GetAsync($"/investigations/{id}")).StatusCode == HttpStatusCode.Unauthorized, "logged-out session rejected");
    Console.WriteLine($"Hub HTTP/PostgreSQL: {checks} checks passed, including OAuth protocol with fake GitHub and real Java/Roslyn + Git worktree validation");
}
finally { Environment.SetEnvironmentVariable("FORGESCOPE_RUNNER_TOKEN", null); await app.StopAsync(); await app.DisposeAsync(); Directory.Delete(directory, true); }

sealed class FakeGitHub(string commit, string patch, Dictionary<string, byte[]> files) : HttpMessageHandler
{
    public bool PkceExchanged { get; private set; }
    public bool LogDownloadWithoutToken { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        object payload; string path = request.RequestUri!.AbsolutePath;
        if (path == "/ci-archive")
        {
            LogDownloadWithoutToken = request.Headers.Authorization is null;
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("build.txt").Open()); writer.Write("Test.java:1: error: missing symbol"); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
        }
        if (path.EndsWith("/actions/runs/99/logs", StringComparison.Ordinal)) return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://fixture.blob.core.windows.net/ci-archive") } };
        if (path.EndsWith("/actions/runs/99", StringComparison.Ordinal)) payload = new { head_sha = new string('b', 40), status = "completed", conclusion = "failure" };
        else if (path == "/login/oauth/access_token")
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            PkceExchanged = form["code_verifier"].ToString().Length >= 43;
            payload = new { access_token = "fake-token-" + form["code"], token_type = "bearer", scope = "read:user" };
        }
        else if (path == "/user") { bool alice = request.Headers.Authorization!.Parameter!.EndsWith("alice", StringComparison.Ordinal); payload = new { id = alice ? 101 : 202, login = alice ? "alice" : "bob" }; }
        else if (path == "/user/repos") payload = new[] { new { full_name = "fixture/repo", language = "Java", description = "Integration fixture", stargazers_count = 0 } };
        else if (path.EndsWith("/pulls/1", StringComparison.Ordinal))
        {
            if (request.Headers.Accept.Any(a => a.MediaType == "application/vnd.github.diff")) return new(HttpStatusCode.OK) { Content = new StringContent(patch) };
            payload = new { head = new { sha = commit, repo = new { full_name = "fixture/repo" } } };
        }
        else if (path.Contains("/git/trees/", StringComparison.Ordinal)) payload = new { truncated = false, tree = files.Select((f, i) => new { path = f.Key, type = "blob", mode = "100644", size = f.Value.Length, sha = i.ToString() }) };
        else if (path.Contains("/git/blobs/", StringComparison.Ordinal)) payload = new { encoding = "base64", content = Convert.ToBase64String(files.ElementAt(int.Parse(path.Split('/').Last())).Value) };
        else if (path == "/repos/fixture/repo") payload = new { default_branch = "main" };
        else throw new Exception("Unexpected GitHub request " + path);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
