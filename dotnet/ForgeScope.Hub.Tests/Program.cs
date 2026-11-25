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
    // A schema owner can migrate its own objects without database-wide CREATE permission.
    await using (var permissions = app.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand("CREATE ROLE hub_schema_owner LOGIN PASSWORD 'fixture-only'; ALTER SCHEMA forgescope OWNER TO hub_schema_owner; " + string.Join(";", new[] { "sessions", "investigations", "runner_tokens", "data_protection_keys", "repo_watches", "notifications", "monitored_runs" }.Select(t => "ALTER TABLE forgescope." + t + " OWNER TO hub_schema_owner")))) await permissions.ExecuteNonQueryAsync();
    var scopedOptions = new NpgsqlConnectionStringBuilder(database) { Username = "hub_schema_owner", Password = "fixture-only" };
    await using (var scoped = NpgsqlDataSource.Create(scopedOptions.ConnectionString))
    {
        await using var privileges = scoped.CreateCommand("SELECT has_database_privilege(current_user,current_database(),'CREATE')");
        Check(!(bool)(await privileges.ExecuteScalarAsync())!, "migration role has no database-wide CREATE");
        await new Store(scoped).Migrate(); Check(true, "existing schema migrates under schema-scoped ownership");
    }
    await using (var role = app.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand("CREATE ROLE forgescope_app LOGIN PASSWORD 'fixture-only'; GRANT USAGE ON SCHEMA forgescope TO forgescope_app")) await role.ExecuteNonQueryAsync();
    await using (var migration = app.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(await File.ReadAllTextAsync("docs/NOTIFICATION-MIGRATION.sql"))) await migration.ExecuteNonQueryAsync();
    var dmlOptions = new NpgsqlConnectionStringBuilder(database) { Username = "forgescope_app", Password = "fixture-only" };
    await using (var dml = NpgsqlDataSource.Create(dmlOptions.ConnectionString))
    {
        await using var privileges = dml.CreateCommand("SELECT has_schema_privilege(current_user,'forgescope','CREATE')");
        Check(!(bool)(await privileges.ExecuteScalarAsync())!, "application role remains DML-only after notification migration");
        var dmlStore = new Store(dml); await dmlStore.Watch("dml-fixture", "fixture/repo");
        Check((await dmlStore.Watches("dml-fixture")).Count == 1, "restricted role can monitor after explicit migration");
        var evidence = new Alert(Investigation.Hash("fixture"), "fixture/repo", 1, 1, new string('a', 40), "CI", "Build", "Compile", "fixture diagnostic", "file.ts", 1, "annotation", DateTimeOffset.UtcNow);
        await dmlStore.SaveRunAlerts("dml-fixture", "fixture/repo", 1, 1, [evidence]);
        Check(await dmlStore.SeenRun("dml-fixture", "fixture/repo", 1, 1) && (await dmlStore.Notifications("dml-fixture")).Count == 1, "restricted role can persist notification evidence");
        await dmlStore.Unwatch("dml-fixture", "fixture/repo");
    }
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
    Check((await anon.GetAsync("/notifications")).StatusCode == HttpStatusCode.Unauthorized, "anonymous inbox rejected");
    Check((await anon.GetAsync("/repositories")).StatusCode == HttpStatusCode.Unauthorized, "anonymous repository access rejected");
    Check((await anon.PostAsync("/api/runner/claim", new StringContent("{}"))).StatusCode == HttpStatusCode.Unauthorized, "runner requires bearer");
    using (var spoof = new HttpRequestMessage(HttpMethod.Get, "/auth/github")) { spoof.Headers.Host = "evil.invalid"; Check((await anon.SendAsync(spoof)).StatusCode == HttpStatusCode.BadRequest, "spoofed Host rejected"); }
    await SignIn(alice, "alice"); await SignIn(bob, "bob"); Check(fake.PkceExchanged, "verifier exchanged on server");
    using (var scopeRequest = await bob.GetAsync("/auth/github/notifications?lang=pt-BR"))
    {
        var scope = QueryHelpers.ParseQuery(scopeRequest.Headers.Location!.Query);
        Check(scope["scope"].ToString().Split(' ').Contains("notifications") && scope["code_challenge_method"] == "S256", "notification permission upgrade uses OAuth PKCE and explicit scope");
    }
    string notices = await alice.GetStringAsync("/notifications?lang=pt-BR");
    Check(notices.Contains("Fixture merged PR", StringComparison.Ordinal) && notices.Contains("Merge realizado", StringComparison.Ordinal), "GitHub inbox includes PR state from subject API");
    Check(notices.IndexOf("Suas notificações do GitHub", StringComparison.Ordinal) < notices.IndexOf("Erros e monitoramento", StringComparison.Ordinal), "GitHub inbox precedes repository monitoring");
    string bobNotices = await bob.GetStringAsync("/notifications?lang=en");
    Check(bobNotices.Contains("Bob private title", StringComparison.Ordinal) && !bobNotices.Contains("Fixture merged PR", StringComparison.Ordinal), "provider inbox uses each signed-in user's token");
    fake.DenyNotifications = true;
    string deniedInbox = await alice.GetStringAsync("/notifications?lang=pt-BR");
    Check(deniedInbox.Contains("Autorizar notificações do GitHub", StringComparison.Ordinal) && deniedInbox.Contains("Erros e monitoramento", StringComparison.Ordinal), "missing notifications scope keeps monitoring available with opt-in authorization");
    fake.DenyNotifications = false;
    string publicHome = await anon.GetStringAsync("/?lang=en");
    Check(publicHome.Contains("Choose file", StringComparison.Ordinal) == false && publicHome.Contains("Start interactive tour", StringComparison.Ordinal) && publicHome.Contains("DevRescue", StringComparison.Ordinal), "module introduction and interactive tour available before sign-in");
    using (var script = await anon.GetAsync("/assets/hub.js")) Check(script.IsSuccessStatusCode && script.Content.Headers.ContentType?.MediaType == "text/javascript", "same-origin interactive script served anonymously");
    Check((await anon.GetAsync("/catalog?lang=en")).IsSuccessStatusCode, "RevScope tool catalog available without sign-in");
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
    using (var watch = await alice.PostAsync("/notifications/watch", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf, ["repository"] = "fixture/repo" }))) Check(watch.StatusCode == HttpStatusCode.Redirect, "watch accessible repository");
    var notifications = app.Services.GetRequiredService<NotificationSync>();
    await notifications.Sync("101", "fixture/repo", "fake-token-alice", CancellationToken.None);
    var alerts = await store.Notifications("101"); Check(alerts.Count == 3, "annotations, language-neutral logs and unknown diagnosis persisted");
    Check(alerts.Any(a => a.Path == "src/main.ts" && a.Line == 12 && a.Message.Contains("TS2304", StringComparison.Ordinal)), "TypeScript diagnostic with exact provider location");
    Check(alerts.Any(a => a.Path == "main.go" && a.Line == 7 && a.EvidenceKind == "log"), "Go diagnostic from job log");
    Check(alerts.Any(a => a.EvidenceKind == "step" && a.Path == ""), "missing evidence remains unknown");
    Check(alerts.All(a => !a.Message.Contains("ghp_", StringComparison.Ordinal) && !a.Message.Contains("secret-value", StringComparison.Ordinal)), "common credentials redacted before persistence");
    await store.SaveRunAlerts("101", "fixture/repo", 55, 2, alerts); Check((await store.Notifications("101")).Count == 3, "duplicate delivery is idempotent");
    Check(await store.SeenRun("101", "fixture/repo", 55, 2) && !await store.SeenRun("101", "fixture/repo", 55, 3), "rerun identity includes attempt");
    string inbox = await alice.GetStringAsync("/notifications?lang=pt-BR&q=TS2304");
    Check(inbox.Contains("Notifica", StringComparison.Ordinal) && inbox.Contains("TS2304", StringComparison.Ordinal) && !inbox.Contains("<script>diagnostic</script>", StringComparison.Ordinal), "localized searchable inbox escapes source evidence");
    foreach (string theme in new[] { "black", "dark", "light" })
    {
        string panel = await alice.GetStringAsync($"/notifications?lang=pt-BR&theme={theme}&filter=unread&repository=fixture%2Frepo&q=TS2304");
        Check(panel.Contains($"body class=\"{theme}\"", StringComparison.Ordinal) && panel.Contains("Painel de Notificações", StringComparison.Ordinal), "localized error dashboard uses selected theme");
        Check(panel.Contains("Diagnóstico original", StringComparison.Ordinal) && panel.Contains("O que verificar", StringComparison.Ordinal) && panel.Contains("TS2304", StringComparison.Ordinal), "localized guidance preserves original diagnostic");
        string settingsHref = WebUtility.HtmlDecode(Regex.Match(panel, "href=\"([^\"]+)\" lang=\"en\"").Groups[1].Value);
        var query = QueryHelpers.ParseQuery(new Uri(new Uri(origin), settingsHref).Query);
        Check(query["q"] == "TS2304" && query["repository"] == "fixture/repo" && query["filter"] == "unread" && query["theme"] == theme && query["lang"] == "en", "language switch preserves filters and theme");
        string persisted = await alice.GetStringAsync("/repositories");
        Check(persisted.Contains($"body class=\"{theme}\"", StringComparison.Ordinal) && persisted.Contains("lang=\"pt-BR\"", StringComparison.Ordinal), "theme and language persist across pages");
    }
    using (var badForm = new HttpRequestMessage(HttpMethod.Post, "/notifications/watch?lang=pt-BR&theme=black"))
    {
        badForm.Headers.Accept.ParseAdd("text/html"); badForm.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["repository"] = "fixture/repo" });
        using var response = await alice.SendAsync(badForm); string html = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == HttpStatusCode.Forbidden && html.Contains("Este formulário expirou", StringComparison.Ordinal) && html.Contains("body class=\"black\"", StringComparison.Ordinal), "browser errors use the chosen locale and theme");
    }
    Check(!(await bob.GetStringAsync("/notifications")).Contains("TS2304", StringComparison.Ordinal), "cross-account inbox isolation");
    async Task<HttpStatusCode> AlertAction(HttpClient client, string key, string action) { using var result = await client.PostAsync($"/notifications/{key}/{action}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf })); return result.StatusCode; }
    Check(await AlertAction(alice, alerts[0].Key, "read") == HttpStatusCode.Redirect && await AlertAction(alice, alerts[0].Key, "save") == HttpStatusCode.Redirect, "read and saved state stored");
    Check((await store.Notifications("101")).Single(a => a.Key == alerts[0].Key) is { Read: true, Saved: true }, "read/saved state survives reload");
    Check(!await store.NotificationState("202", alerts[0].Key, "done"), "cross-account state mutation rejected");
    Check(await AlertAction(alice, alerts[0].Key, "done") == HttpStatusCode.Redirect, "done state");
    Check(!await store.LeaseWatch("101", "fixture/repo"), "polling lease prevents duplicate API work");
    using (var missingCsrf = await alice.PostAsync("/notifications/watch", new FormUrlEncodedContent(new Dictionary<string, string> { ["repository"] = "fixture/repo" }))) Check(missingCsrf.StatusCode == HttpStatusCode.Forbidden, "monitor changes require CSRF");
    using (var bad = await alice.PostAsync("/investigations", new FormUrlEncodedContent(new Dictionary<string, string> { ["repository"] = "fixture/repo" }))) Check(bad.StatusCode == HttpStatusCode.Forbidden, "missing CSRF rejected");
    using var created = await alice.PostAsync("/investigations", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf, ["repository"] = "fixture/repo", ["pr"] = "1", ["run"] = "99", ["log"] = "Test.java:1: error: missing symbol", ["lang"] = "es" }));
    Check(created.StatusCode == HttpStatusCode.Redirect, "GitHub snapshot through real Java/Roslyn analyzer: " + await created.Content.ReadAsStringAsync());
    var id = Guid.Parse(created.Headers.Location!.ToString().Split('/')[2].Split('?')[0]);
    Check((await bob.GetAsync($"/investigations/{id}")).StatusCode == HttpStatusCode.NotFound, "cross-account isolation");
    Check((await bob.GetAsync($"/investigations/{id}/export")).StatusCode == HttpStatusCode.NotFound, "cross-account export isolation");
    using (var md = await alice.GetAsync($"/investigations/{id}/export?format=markdown")) Check(md.IsSuccessStatusCode && md.Content.Headers.ContentDisposition?.FileNameStar == "forgescope-investigation.md" && (await md.Content.ReadAsStringAsync()).Contains("# ForgeScope investigation", StringComparison.Ordinal), "Markdown findings export");
    using (var sarif = await alice.GetAsync($"/investigations/{id}/export?format=sarif"))
    { using var doc = JsonDocument.Parse(await sarif.Content.ReadAsStringAsync()); Check(sarif.IsSuccessStatusCode && doc.RootElement.GetProperty("version").GetString() == "2.1.0" && doc.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength() > 0, "SARIF findings export"); }
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
    string home = await bob.GetStringAsync("/?lang=pt-BR&theme=black");
    Check(home.Contains("Analisar ZIP", StringComparison.Ordinal) && home.Contains("Tour guiado interativo", StringComparison.Ordinal) && !home.Contains("#97efc2", StringComparison.Ordinal), "welcoming localized upload and tour with blue branding");
    string bobCsrf = WebUtility.HtmlDecode(Regex.Match(home, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    using (var uploadDenied = await anon.PostAsync("/source", new FormUrlEncodedContent(new Dictionary<string, string>()))) Check(uploadDenied.StatusCode == HttpStatusCode.Unauthorized, "source upload requires authentication");
    using (var uploadCsrf = await bob.PostAsync("/source", new FormUrlEncodedContent(new Dictionary<string, string> { ["filename"] = "example.py", ["code"] = "print('hello')" }))) Check(uploadCsrf.StatusCode == HttpStatusCode.Forbidden, "source upload requires CSRF");
    using (var pasted = await bob.PostAsync("/source", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = bobCsrf, ["filename"] = "example.py", ["code"] = "print('hello')", ["lang"] = "pt-BR" })))
    {
        Check(pasted.StatusCode == HttpStatusCode.Redirect, "pasted code processed by real analyzer");
        Guid localId = Guid.Parse(pasted.Headers.Location!.ToString().Split('/')[2].Split('?')[0]);
        var local = (await store.Get("202", localId))!;
        Check(local.Origin == "local" && local.Commit.Length == 64 && local.Report.GetProperty("findings").ValueKind == JsonValueKind.Array, "local report has source digest and honest origin");
        Check(local.Report.GetProperty("input_scope").GetString() == "snippet" && !local.Report.GetProperty("findings").EnumerateArray().Any(f => f.GetProperty("rule").GetString() is "docs.readme" or "ci.missing" or "license.missing"), "isolated snippet does not inherit missing repository metadata warnings");
        string detail = await bob.GetStringAsync(pasted.Headers.Location);
        Check(detail.Contains("Análise de código local", StringComparison.Ordinal) && !detail.Contains("github.com/local/source/blob", StringComparison.Ordinal), "local results have no fabricated GitHub source links");
        Check((await alice.GetAsync(pasted.Headers.Location)).StatusCode == HttpStatusCode.NotFound, "local reports isolated by account");
    }
    async Task<HttpResponseMessage> UploadZip(string path)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(archive.CreateEntry(path).Open()); writer.Write("print('fixture')"); }
        using var content = new MultipartFormDataContent(); content.Add(new StringContent(bobCsrf), "__RequestVerificationToken"); content.Add(new ByteArrayContent(bytes.ToArray()), "archive", "project.zip");
        return await bob.PostAsync("/source", content);
    }
    using (var zipResult = await UploadZip("src/example.py")) Check(zipResult.StatusCode == HttpStatusCode.Redirect, "ZIP project processed by real analyzer");
    using (var unsafeZip = await UploadZip("../escape.py")) Check(unsafeZip.StatusCode == HttpStatusCode.Conflict, "ZIP traversal rejected");
    Check(!Directory.EnumerateDirectories(Path.GetTempPath(), "forgescope-upload-*").Any(), "temporary uploaded sources removed after success and rejection");
    var processStatus = new Alert("generic", "fixture/repo", 77, 1, commit, "CI", "Download", "Fetch", "Process completed with exit code 1.", ".github", 13, "annotation", DateTimeOffset.UtcNow);
    var rateLimit = processStatus with { Key = "rate", Message = "Dependency download HTTP 429", Path = "", Line = null };
    var viewCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext(); viewCtx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?lang=pt-BR");
    string grouped = Views.Inbox(viewCtx, "fixture", [processStatus, rateLimit], []);
    Check(grouped.IndexOf("Dependency download HTTP 429", StringComparison.Ordinal) < grouped.IndexOf("Process completed with exit code 1.", StringComparison.Ordinal) && !grouped.Contains(".github:13", StringComparison.Ordinal), "specific diagnostic precedes generic status without fabricated source location");
    Check(grouped.Contains("Retry-After", StringComparison.Ordinal) && grouped.Contains("target=\"_blank\"", StringComparison.Ordinal), "rate-limit explanation in dashboard and external evidence opens new tab");
    string englishHome = await bob.GetStringAsync("/?lang=en");
    Check(englishHome.Contains("Choose file", StringComparison.Ordinal) && englishHome.Contains("No file selected", StringComparison.Ordinal) && englishHome.Contains("data-file-input", StringComparison.Ordinal) && englishHome.Contains("id=\"guided-tour\"", StringComparison.Ordinal), "localized custom picker and interactive tour markup");
    using (var fifth = await bob.PostAsync("/source", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = bobCsrf }))) Check(fifth.StatusCode == HttpStatusCode.Conflict, "invalid scan rejected before analysis");
    using (var limited = await bob.PostAsync("/source?lang=pt-BR", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = bobCsrf })))
    { string limitedHtml = await limited.Content.ReadAsStringAsync(); Check(limited.StatusCode == HttpStatusCode.TooManyRequests && limited.Headers.RetryAfter is not null && limitedHtml.Contains("Muitas solicitações", StringComparison.Ordinal) && limitedHtml.Contains("data-retry", StringComparison.Ordinal), "rate-limit response is localized HTML with retry countdown"); }
    var artifactReport = JsonSerializer.SerializeToElement(new { findings = Array.Empty<object>(), artifacts = new { signals = new[] { new { path = "captures/request.pcap", kind = "Recognized capture header", verification = "pcap header", notes = "Use a packet viewer only on authorized captures.", tool_ids = new[] { "wireshark" } } } } });
    var recommendationCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext(); recommendationCtx.Request.QueryString = new("?lang=pt-BR");
    string recommendations = Views.Catalog(recommendationCtx, "", [new Investigation { Title="Artifact fixture", Report=artifactReport }]);
    Check(recommendations.Contains("Wireshark", StringComparison.Ordinal) && recommendations.Contains("captures/request.pcap", StringComparison.Ordinal) && recommendations.Contains("Ver evidência da indicação", StringComparison.Ordinal), "RevScope recommendation ties the catalog tool to project artifact evidence");
    var latest = await Form(alice, id);
    using var logout = await alice.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = latest.Token })); Check(logout.StatusCode == HttpStatusCode.Redirect, "logout");
    Check((await alice.GetAsync($"/investigations/{id}")).StatusCode == HttpStatusCode.Unauthorized, "logged-out session rejected");
    Console.WriteLine($"Hub HTTP/PostgreSQL: {checks} checks passed, including OAuth protocol with fake GitHub and real Java/Roslyn + Git worktree validation");
}
finally { Environment.SetEnvironmentVariable("FORGESCOPE_RUNNER_TOKEN", null); await app.StopAsync(); await app.DisposeAsync(); Directory.Delete(directory, true); }

sealed class FakeGitHub(string commit, string patch, Dictionary<string, byte[]> files) : HttpMessageHandler
{
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    public bool DenyNotifications { get; set; }
    public bool PkceExchanged { get; private set; }
    public bool LogDownloadWithoutToken { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        object payload; string path = request.RequestUri!.AbsolutePath;
        if (path == "/notifications")
        {
            if (DenyNotifications) return new(HttpStatusCode.Forbidden);
            bool alice = request.Headers.Authorization?.Parameter?.EndsWith("alice", StringComparison.Ordinal) == true;
            return Json(new[] { new { id="thread-1", repository=new { full_name="fixture/repo" }, unread=true, updated_at="2026-11-25T00:00:00Z", reason="review_requested", subject=new { type="PullRequest", title=alice ? "Fixture merged PR" : "Bob private title", url="https://api.github.com/repos/fixture/repo/pulls/7" } } });
        }
        if (path == "/repos/fixture/repo/pulls/7") return Json(new { state="closed", merged_at="2026-11-25T00:00:00Z" });
        if (path == "/ci-archive")
        {
            LogDownloadWithoutToken = request.Headers.Authorization is null;
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("build.txt").Open()); writer.Write("Test.java:1: error: missing symbol"); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
        }
        if (path == "/repos/fixture/repo/actions/runs") return Json(new { workflow_runs = new[] { new { id = 55, run_attempt = 2, head_sha = commit, name = "CI", conclusion = "failure" } } });
        if (path == "/repos/fixture/repo/actions/runs/55/attempts/2/jobs") return Json(new { total_count = 3, jobs = new[] {
            new { id = 123, name = "TypeScript", conclusion = "failure", check_run_url = "https://api.github.com/repos/fixture/repo/check-runs/123", steps = new[] { new { name = "Compile", conclusion = "failure" } } },
            new { id = 124, name = "Go", conclusion = "failure", check_run_url = "", steps = new[] { new { name = "Build", conclusion = "failure" } } },
            new { id = 125, name = "Ruby", conclusion = "failure", check_run_url = "", steps = new[] { new { name = "Test", conclusion = "failure" } } } } });
        if (path == "/repos/fixture/repo/check-runs/123/annotations") return Json(new[] { new { annotation_level = "failure", path = "src/main.ts", start_line = 12, message = "TS2304: Cannot find name value. <script>diagnostic</script> token=secret-value ghp_fixturetesttoken1234567890" } });
        if (path == "/repos/fixture/repo/actions/jobs/124/logs") return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://fixture.blob.core.windows.net/job-log") } };
        if (path == "/job-log") { if (request.Headers.Authorization is not null) throw new Exception("Provider token leaked"); return new(HttpStatusCode.OK) { Content = new StringContent("main.go:7:2: undefined: missingName") }; }
        if (path == "/repos/fixture/repo/actions/jobs/125/logs") return new(HttpStatusCode.Forbidden);
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
