using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForgeScope.Hub;

public sealed class GitHubApiException(int status) : InvalidOperationException($"GitHub returned HTTP {status}")
{
    public int Status { get; } = status;
}
public sealed record GitHubNotice(string Id, string Repository, string Title, string Type, string Reason, bool Unread, string Updated, string Url, string State);
public sealed record GitHubInbox(List<GitHubNotice> Items, int Page, bool More, string Error = "");

public static class GitHubNotifications
{
    public static async Task<GitHubInbox> Load(GitHub github, string token, int page, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 1000);
        try
        {
            var rows = await github.Json($"/notifications?all=true&per_page=50&page={page}", token, ct);
            if (rows.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Invalid notification response");
            var notices = new List<GitHubNotice>(); int details = 0;
            foreach (var row in rows.EnumerateArray())
            {
                var subject = row.GetProperty("subject"); var repo = row.GetProperty("repository");
                string repository = Investigation.RepositoryName(Views.Text(repo, "full_name"));
                string type = Views.Text(subject, "type"), api = Views.Text(subject, "url"), state = "";
                string url = $"https://github.com/notifications?query=repo%3A{Uri.EscapeDataString(repository)}";
                var match = Regex.Match(api, "^https://api\\.github\\.com/repos/" + Regex.Escape(repository) + "/(issues|pulls|commits)/([A-Za-z0-9]+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
                if (match.Success)
                {
                    string route = match.Groups[1].Value, id = match.Groups[2].Value;
                    url = $"https://github.com/{repository}/{(route == "issues" ? "issues" : route == "pulls" ? "pull" : "commit")}/{id}";
                    if (route == "pulls" && details++ < 10)
                    {
                        try
                        {
                            var pr = await github.Json(api["https://api.github.com".Length..], token, ct);
                            state = pr.TryGetProperty("merged_at", out var merged) && merged.ValueKind != JsonValueKind.Null ? "merged" : Views.Text(pr, "state");
                        }
                        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { /* Notification remains available even without PR details. */ }
                    }
                }
                notices.Add(new(Views.Text(row, "id"), repository, Views.Text(subject, "title"), type, Views.Text(row, "reason"),
                    row.TryGetProperty("unread", out var unread) && unread.ValueKind == JsonValueKind.True, Views.Text(row, "updated_at"), url, state));
            }
            return new(notices, page, notices.Count == 50);
        }
        catch (GitHubApiException ex) when (ex.Status is 401 or 403 or 404) { return new([], page, false, "permission"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException) { return new([], page, false, "unavailable"); }
    }
}
