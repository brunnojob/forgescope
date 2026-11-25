using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ForgeScope.Hub;

public static class NotificationRoutes
{
    public static void MapNotifications(this WebApplication app)
    {
        app.MapGet("/notifications", async (HttpContext ctx, Store store, IAntiforgery csrf) =>
            Results.Content(Views.Inbox(ctx, csrf.GetAndStoreTokens(ctx).RequestToken!, await store.Notifications(Owner(ctx)), await store.Watches(Owner(ctx))), "text/html; charset=utf-8")).RequireAuthorization();
        app.MapPost("/notifications/watch", async (HttpContext ctx, Store store, GitHub github, IDataProtectionProvider protection, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync(); string repo = Investigation.RepositoryName(form["repository"].ToString()).ToLowerInvariant();
            if (form["action"] == "stop") await store.Unwatch(Owner(ctx), repo);
            else
            {
                var session = await store.Session(ctx.User.FindFirstValue("session_id")!) ?? throw new KeyNotFoundException();
                await github.Json($"/repos/{repo}", protection.CreateProtector("github-token-v1").Unprotect(session.ProtectedToken), ctx.RequestAborted);
                await store.Watch(Owner(ctx), repo);
            }
            return Results.Redirect("/notifications?lang=" + Views.Language(ctx));
        }).RequireAuthorization().RequireRateLimiting("scan");
        app.MapPost("/notifications/sync", async (HttpContext ctx, Store store, NotificationSync sync, IDataProtectionProvider protection, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx); var form = await ctx.Request.ReadFormAsync();
            var session = await store.Session(ctx.User.FindFirstValue("session_id")!) ?? throw new KeyNotFoundException();
            var token = protection.CreateProtector("github-token-v1").Unprotect(session.ProtectedToken);
            // Each request is bounded; background polling continues the remaining watches.
            foreach (var watch in (await store.Watches(Owner(ctx))).Where(w => string.IsNullOrEmpty(form["repository"]) || w.Repository == form["repository"].ToString()).Take(5))
                await sync.Sync(Owner(ctx), watch.Repository, token, ctx.RequestAborted);
            return Results.Redirect("/notifications?lang=" + Views.Language(ctx));
        }).RequireAuthorization().RequireRateLimiting("scan");
        app.MapPost("/notifications/{key}/{action}", async (string key, string action, HttpContext ctx, Store store, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx);
            if (!Regex.IsMatch(key, "^[a-f0-9]{64}$")) return Results.BadRequest();
            return await store.NotificationState(Owner(ctx), key, action) ? Results.Redirect("/notifications?lang=" + Views.Language(ctx)) : Results.NotFound();
        }).RequireAuthorization();
    }
    static string Owner(HttpContext ctx) => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
