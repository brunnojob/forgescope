using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using System.Threading.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace ForgeScope.Hub;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--runner")) return await Runner.Run(args);
        if (args.Contains("--self-test")) return Checks.Run();
        var app = CreateApp(args.Where(a => a != "--migrate").ToArray());
        if (args.Contains("--migrate")) { await app.Services.GetRequiredService<Store>().Migrate(); Console.WriteLine("ForgeScope schema ready"); await app.DisposeAsync(); return 0; }
        await app.RunAsync(); return 0;
    }
    // Explicit dependency injection is used by HTTP integration tests; no environment-based auth bypass exists.
    public static WebApplication CreateApp(string[] args, Action<WebApplicationBuilder>? configure = null, HttpMessageHandler? githubTransport = null)
    {
        var builder = WebApplication.CreateBuilder(args); configure?.Invoke(builder);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        var config = builder.Configuration;
        var origin = new Uri(config["FORGESCOPE_PUBLIC_URL"] ?? "http://127.0.0.1:8766");
        if (origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 || origin.UserInfo.Length > 0 ||
            origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback)) throw new InvalidOperationException("Public URL must be an HTTPS origin (HTTP loopback allowed)");
        bool secure = origin.Scheme == "https";
        var connection = config["FORGESCOPE_DATABASE"] ?? throw new InvalidOperationException("FORGESCOPE_DATABASE is required");
        var databaseOptions = new NpgsqlConnectionStringBuilder(connection);
        if (databaseOptions.SslMode != SslMode.VerifyFull && databaseOptions.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Remote PostgreSQL requires SSL Mode=VerifyFull");
        var database = NpgsqlDataSource.Create(connection);
        builder.Services.AddSingleton(database); builder.Services.AddSingleton<Store>(); builder.Services.AddSingleton<Analyzer>();
        var protection = builder.Services.AddDataProtection().SetApplicationName("ForgeScope.Hub");
        if (config["FORGESCOPE_KEY_CERTIFICATE"] is { Length: > 0 } certificate)
        {
            var cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificate), null, X509KeyStorageFlags.EphemeralKeySet);
            if (!cert.HasPrivateKey || cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidOperationException("A valid private key-encryption certificate is required");
            protection.ProtectKeysWithCertificate(cert);
            builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new PostgresKeys(database));
        }
        else
        {
            var keys = config["FORGESCOPE_KEYS"] ?? throw new InvalidOperationException("Configure a persistent key directory or a private key-encryption certificate");
            protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        }
        builder.Services.Configure<FormOptions>(o => { o.ValueLengthLimit = 2_100_000; o.MultipartBodyLengthLimit = 4_000_000; });
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 4_000_000);
        builder.Services.AddSingleton(new GitHub(new HttpClient(githubTransport ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) }));
        builder.Services.AddAntiforgery(o => { o.Cookie.Name = secure ? "__Host-ForgeScope.Csrf" : "ForgeScope.Csrf"; o.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.None; o.Cookie.SameSite = SameSiteMode.Strict; });
        builder.Services.AddAuthentication(o => { o.DefaultScheme = "session"; o.DefaultChallengeScheme = "session"; })
            .AddCookie("session", o =>
            {
                o.Cookie.Name = secure ? "__Host-ForgeScope.Session" : "ForgeScope.Session";
                o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.None; o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    string? id = ctx.Principal?.FindFirstValue("session_id");
                    if (id is null || await ctx.HttpContext.RequestServices.GetRequiredService<Store>().Session(id) is null) ctx.RejectPrincipal();
                };
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            })
            .AddOAuth("github", o =>
            {
                o.SignInScheme = "session"; o.ClientId = config["GITHUB_CLIENT_ID"] ?? "unconfigured"; o.ClientSecret = config["GITHUB_CLIENT_SECRET"] ?? "unconfigured";
                o.CallbackPath = "/signin-github"; o.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                o.TokenEndpoint = "https://github.com/login/oauth/access_token"; o.UserInformationEndpoint = "https://api.github.com/user";
                o.UsePkce = true; o.SaveTokens = false; o.Scope.Add("read:user");
                if (config["FORGESCOPE_PRIVATE_REPOS"] == "true") o.Scope.Add("repo");
                o.CorrelationCookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
                o.CorrelationCookie.SameSite = SameSiteMode.Lax; o.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
                if (githubTransport is not null) o.Backchannel = new HttpClient(githubTransport, disposeHandler: false);
                o.Events = new OAuthEvents
                {
                    OnCreatingTicket = async ctx =>
                    {
                        var services = ctx.HttpContext.RequestServices;
                        var profile = await services.GetRequiredService<GitHub>().Json("/user", ctx.AccessToken!, ctx.HttpContext.RequestAborted);
                        var userId = profile.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var login = profile.GetProperty("login").GetString()!; string session = Investigation.RandomToken();
                        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("github-token-v1");
                        var expires = DateTimeOffset.UtcNow.AddHours(8);
                        if (double.TryParse(ctx.TokenResponse.ExpiresIn, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                        { if (seconds <= 0 || seconds > 31_536_000) throw new InvalidOperationException("Invalid OAuth token lifetime"); expires = new[] { expires, DateTimeOffset.UtcNow.AddSeconds(seconds) }.Min(); }
                        await services.GetRequiredService<Store>().SaveSession(new(session, userId, login, protector.Protect(ctx.AccessToken!), expires));
                        ctx.Identity!.AddClaim(new(ClaimTypes.NameIdentifier, userId)); ctx.Identity.AddClaim(new(ClaimTypes.Name, login)); ctx.Identity.AddClaim(new("session_id", session));
                        ctx.Properties.ExpiresUtc = expires;
                    },
                    OnRemoteFailure = ctx => { ctx.HandleResponse(); ctx.Response.Redirect("/?auth=failed"); return Task.CompletedTask; }
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<NotificationSync>();
        builder.Services.AddHostedService<NotificationPoller>();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("scan", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
        });
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            // A configured canonical origin prevents Host/forwarded-header spoofing in OAuth redirects.
            if (!string.Equals(ctx.Request.Host.Host, origin.Host, StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 400; return; }
            ctx.Request.Scheme = origin.Scheme; ctx.Request.Host = new HostString(origin.Authority);
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'self' 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
            ctx.Response.Headers.XContentTypeOptions = "nosniff"; ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers.CacheControl = "no-store";
            try { await next(); }
            catch (AntiforgeryValidationException) { ctx.Response.StatusCode = 403; await WriteError(ctx, 403, "Invalid form token"); }
            catch (KeyNotFoundException) { await WriteError(ctx, 404, "Not found"); }
            catch (Exception ex) when (ex is JsonException or FormatException or BadHttpRequestException or InvalidDataException or DecoderFallbackException)
            { ctx.Response.StatusCode = 400; await WriteError(ctx, 400, "Invalid request input"); }
            catch (InvalidOperationException ex) { ctx.Response.StatusCode = 409; await WriteError(ctx, 409, ex.Message); }
            catch (Exception ex) when (ex is NpgsqlException or HttpRequestException or TaskCanceledException)
            { ctx.Response.StatusCode = 503; await WriteError(ctx, 503, "Service unavailable; retry later"); }
        });
        app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapNotifications();
        app.MapGet("/health", () => Results.Json(new { status = "ok", service = "forgescope-hub" }));
        app.MapGet("/favicon.ico", () => ForgeScope.Workbench.BrandAssets.Get("favicon.ico"));
        app.MapGet("/assets/branding/{name}", (string name) => ForgeScope.Workbench.BrandAssets.Get(name));
        app.MapGet("/ready", async (NpgsqlDataSource db) => { await using var cmd = db.CreateCommand("SELECT count(*) FROM forgescope.sessions WHERE false"); await cmd.ExecuteScalarAsync(); return Results.Ok(); });
        app.MapGet("/auth/github", async ctx =>
        {
            if (string.IsNullOrWhiteSpace(config["GITHUB_CLIENT_ID"]) || string.IsNullOrWhiteSpace(config["GITHUB_CLIENT_SECRET"]))
            { ctx.Response.StatusCode = 503; await WriteError(ctx, 503, "Configure the GitHub OAuth application before signing in"); return; }
            string lang = Views.Language(ctx); await ctx.ChallengeAsync("github", new AuthenticationProperties { RedirectUri = "/?lang=" + lang });
        }).RequireRateLimiting("login");
        app.MapGet("/", async (HttpContext ctx, Store store, IAntiforgery csrf) =>
        {
            var rows = ctx.User.Identity?.IsAuthenticated == true ? await store.List(UserId(ctx)) : [];
            return Html(Views.Dashboard(ctx, csrf.GetAndStoreTokens(ctx).RequestToken!, rows, config["FORGESCOPE_PRIVATE_REPOS"] == "true"));
        });
        app.MapPost("/logout", async (HttpContext ctx, Store store, IAntiforgery csrf) =>
        { await csrf.ValidateRequestAsync(ctx); await store.Logout(ctx.User.FindFirstValue("session_id")!); await ctx.SignOutAsync("session"); return Results.Redirect("/"); }).RequireAuthorization();
        app.MapGet("/catalog", (HttpContext ctx, IAntiforgery csrf) => Html(Views.Catalog(ctx, csrf.GetAndStoreTokens(ctx).RequestToken!))).RequireAuthorization();
        app.MapGet("/repositories", async (HttpContext ctx, Store store, GitHub github, IDataProtectionProvider protection, IAntiforgery csrf) =>
        {
            int page = int.TryParse(ctx.Request.Query["page"], out var p) ? Math.Clamp(p, 1, 1000) : 1;
            var token = await Token(ctx, store, protection); var rows = await github.Repositories(token, page, ctx.RequestAborted);
            return Html(Views.Repositories(ctx, csrf.GetAndStoreTokens(ctx).RequestToken!, rows, page));
        }).RequireAuthorization();
        app.MapPost("/investigations", async (HttpContext ctx, Store store, GitHub github, Analyzer analyzer, IDataProtectionProvider protection, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync();
            string repo = Investigation.RepositoryName(form["repository"].ToString());
            int? pr = string.IsNullOrWhiteSpace(form["pr"]) ? null : int.Parse(form["pr"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
            long? run = string.IsNullOrWhiteSpace(form["run"]) ? null : long.Parse(form["run"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
            string log = form["log"].ToString(); if (Encoding.UTF8.GetByteCount(log) > 2_000_000) return Results.BadRequest();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted); deadline.CancelAfter(TimeSpan.FromMinutes(5));
            var value = await github.Analyze(UserId(ctx), await Token(ctx, store, protection), repo, pr, log, analyzer, deadline.Token, run);
            await store.Create(value); return Results.Redirect(Url(value.Id, form["lang"]));
        }).RequireAuthorization().RequireRateLimiting("scan");
        app.MapPost("/source", async (HttpContext ctx, Store store, Analyzer analyzer, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted); deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var value = await SourceImport.Analyze(UserId(ctx), form, analyzer, deadline.Token);
            await store.Create(value); return Results.Redirect(Url(value.Id, form["lang"]));
        }).RequireAuthorization().RequireRateLimiting("scan");
        app.MapPost("/import", async (HttpContext ctx, Store store, GitHub github, IDataProtectionProvider protection, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync();
            string repo = Investigation.RepositoryName(form["repository"].ToString()), commit = form["commit"].ToString();
            if (!System.Text.RegularExpressions.Regex.IsMatch(commit, "^[a-f0-9]{40}$")) return Results.BadRequest();
            // Verify access even when a local report is imported.
            await github.Json($"/repos/{repo}", await Token(ctx, store, protection), ctx.RequestAborted);
            string text = form["report"].ToString(); if (Encoding.UTF8.GetByteCount(text) > 3_000_000) return Results.BadRequest();
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("schema_version", out var schema) || schema.GetInt32() != 1 ||
                !doc.RootElement.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array) return Results.BadRequest();
            var value = new Investigation { OwnerId = UserId(ctx), Repository = repo, Commit = commit, Title = repo, Report = doc.RootElement.Clone() };
            value.Event(UserId(ctx), "imported-unverified-local-report", commit); await store.Create(value); return Results.Redirect(Url(value.Id, form["lang"]));
        }).RequireAuthorization().RequireRateLimiting("scan");
        app.MapGet("/investigations/{id:guid}", async (Guid id, HttpContext ctx, Store store, IAntiforgery csrf) =>
        {
            var value = await store.Get(UserId(ctx), id); return value is null ? Results.NotFound() : Html(Views.Detail(ctx, csrf.GetAndStoreTokens(ctx).RequestToken!, value));
        }).RequireAuthorization();
        app.MapGet("/investigations/{id:guid}/export", async (Guid id, HttpContext ctx, Store store) =>
        {
            var value = await store.Get(UserId(ctx), id); return value is null ? Results.NotFound() : Results.File(JsonSerializer.SerializeToUtf8Bytes(value), "application/json", "forgescope-investigation.json");
        }).RequireAuthorization();
        app.MapPost("/investigations/{id:guid}/{action}", async (Guid id, string action, HttpContext ctx, Store store, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync();
            if (!int.TryParse(form["version"], out var version)) return Results.BadRequest();
            string owner = UserId(ctx), digest = form["digest"].ToString();
            if (action == "runner-token")
            {
                var existing = await store.Get(owner, id) ?? throw new KeyNotFoundException(); existing.RequireApproved(digest);
                string token = await store.RunnerToken(owner, id); return Html(Views.RunnerToken(ctx, token, existing));
            }
            await store.Update(owner, id, version, value =>
            {
                switch (action)
                {
                    case "revise": value.Revise(form["plan"].ToString(), form["patch"].ToString(), owner); break;
                    case "comment": value.Comment(digest, form["body"].ToString(), form["blocker"] == "true", int.Parse(form["line"].ToString(), System.Globalization.CultureInfo.InvariantCulture), owner); break;
                    case "resolve": value.Resolve(digest, Guid.Parse(form["comment"]!), owner); break;
                    case "decide": value.Decide(digest, form["decision"].ToString(), owner); break;
                    case "queue": value.Queue(digest, owner); break;
                    case "cancel": value.Cancel(Guid.Parse(form["job"]!), owner); break;
                    default: throw new KeyNotFoundException();
                }
            }); return Results.Redirect(Url(id, form["lang"]));
        }).RequireAuthorization();
        app.MapPost("/api/runner/claim", async (HttpContext ctx, Store store) =>
        {
            var identity = await RunnerIdentity(ctx, store); if (identity is null) return Results.Unauthorized();
            (ValidationJob Job, string Lease)? claimed = null;
            var value = await store.Update(identity.Value.Owner, identity.Value.Investigation, null, i => claimed = i.Claim());
            return claimed is null ? Results.NoContent() : Results.Json(new RunnerClaim(value.Id, claimed.Value.Job.Id, value.Repository, claimed.Value.Job.Commit,
                claimed.Value.Job.Digest, claimed.Value.Job.PatchDigest, value.Current.Patch, claimed.Value.Lease));
        });
        app.MapPost("/api/runner/complete", async (HttpContext ctx, Store store) =>
        {
            var identity = await RunnerIdentity(ctx, store); if (identity is null) return Results.Unauthorized();
            var submission = await ctx.Request.ReadFromJsonAsync<RunnerCompletion>();
            if (submission is null || submission.Result is null || submission.Result.Before is null || submission.Result.After is null || string.IsNullOrEmpty(submission.Lease)) return Results.BadRequest();
            await store.Update(identity.Value.Owner, identity.Value.Investigation, null, i => i.Complete(submission.Job, submission.Lease, submission.Result)); return Results.Ok();
        });
        return app;
    }
    static async Task<(string Owner, Guid Investigation)?> RunnerIdentity(HttpContext ctx, Store store)
    {
        var auth = ctx.Request.Headers.Authorization.ToString(); return auth.StartsWith("Bearer ", StringComparison.Ordinal) ? await store.Runner(auth[7..]) : null;
    }
    static Task WriteError(HttpContext ctx, int status, string machineMessage)
    {
        ctx.Response.StatusCode = status;
        if (!ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Headers.Accept.Any(v => v?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(Views.ErrorPage(ctx, status));
        }
        return ctx.Response.WriteAsync(machineMessage);
    }
    static async Task<string> Token(HttpContext ctx, Store store, IDataProtectionProvider protection)
    {
        var session = await store.Session(ctx.User.FindFirstValue("session_id")!) ?? throw new KeyNotFoundException();
        return protection.CreateProtector("github-token-v1").Unprotect(session.ProtectedToken);
    }
    static string UserId(HttpContext ctx) => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Sign in first");
    static string Url(Guid id, string? lang) => $"/investigations/{id}?lang={ForgeScope.Workbench.Localization.Language(lang)}";
    static IResult Html(string value) => Results.Content(value, "text/html", Encoding.UTF8);
}
