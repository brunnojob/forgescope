using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace ForgeScope.Hub;

public sealed record ReviewComment(Guid Id, string Body, bool Blocker, bool Resolved, int Line);
public sealed class Revision
{
    public int Number { get; set; }
    public string Plan { get; set; } = "";
    public string Patch { get; set; } = "";
    public string Digest { get; set; } = "";
    public string Decision { get; set; } = "pending";
    public List<ReviewComment> Comments { get; set; } = [];
}
public sealed record AuditEvent(DateTimeOffset At, string Actor, string Action, string Digest);
public sealed class ValidationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Digest { get; set; } = "";
    public string PatchDigest { get; set; } = "";
    public string Commit { get; set; } = "";
    public string State { get; set; } = "queued";
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? LeaseHash { get; set; }
    public ValidationResult? Result { get; set; }
}
public sealed record TestExit(int? ExitCode, bool TimedOut);
public sealed record ValidationResult(string Digest, string PatchDigest, string Commit, TestExit Before, TestExit After);
public sealed record CiRun(long Id, string Commit, string Status, string Conclusion);
public sealed class Investigation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Origin { get; set; } = "github";
    public string Commit { get; set; } = "";
    public int? PullRequest { get; set; }
    public CiRun? Ci { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public JsonElement Report { get; set; }
    public List<Revision> Revisions { get; set; } = [];
    public List<ValidationJob> Jobs { get; set; } = [];
    public List<AuditEvent> Events { get; set; } = [];
    [JsonIgnore] public Revision Current => Revisions.Last();
    public string Stage => Revisions.Count == 0 ? "investigation" :
        Current.Decision != "approved" ? "review" :
        Jobs.LastOrDefault(j => j.Digest == Current.Digest)?.State ?? "approved";

    public void Revise(string plan, string patch, string actor)
    {
        if (string.IsNullOrWhiteSpace(plan) || Encoding.UTF8.GetByteCount(plan) > 64_000 || Encoding.UTF8.GetByteCount(patch) > 2_000_000)
            throw new InvalidOperationException("Invalid plan or patch size");
        var digest = Hash(JsonSerializer.Serialize(new { Commit, plan, patch }));
        if (Revisions.Count > 0 && Current.Digest == digest) return;
        if (Revisions.Count >= 100) throw new InvalidOperationException("Revision limit reached");
        Revisions.Add(new() { Number = Revisions.Count + 1, Plan = plan, Patch = patch, Digest = digest });
        Event(actor, "revision", digest);
    }
    public void Comment(string digest, string body, bool blocker, int line, string actor)
    {
        RequireDigest(digest);
        if (Current.Decision != "pending" || string.IsNullOrWhiteSpace(body) || body.Length > 4000 || Current.Comments.Count >= 200 ||
            line < 1 || line > Current.Plan.Split('\n').Length) throw new InvalidOperationException("Invalid comment or closed review");
        Current.Comments.Add(new(Guid.NewGuid(), body, blocker, false, line)); Event(actor, "comment", digest);
    }
    public void Resolve(string digest, Guid comment, string actor)
    {
        RequireDigest(digest);
        if (Current.Decision != "pending") throw new InvalidOperationException("Review already decided");
        var i = Current.Comments.FindIndex(c => c.Id == comment);
        if (i < 0) throw new InvalidOperationException("Comment not found");
        Current.Comments[i] = Current.Comments[i] with { Resolved = true }; Event(actor, "resolve", digest);
    }
    public void Decide(string digest, string decision, string actor)
    {
        RequireDigest(digest);
        if (decision is not ("approved" or "rejected") || Current.Decision != "pending") throw new InvalidOperationException("Invalid review decision");
        if (decision == "approved" && Current.Comments.Any(c => c.Blocker && !c.Resolved)) throw new InvalidOperationException("Resolve blockers first");
        Current.Decision = decision; Event(actor, decision, digest);
    }
    public void Queue(string digest, string actor)
    {
        if (Origin != "github") throw new InvalidOperationException("Local source snapshots cannot use GitHub runner validation");
        RequireApproved(digest);
        if (string.IsNullOrWhiteSpace(Current.Patch)) throw new InvalidOperationException("A reviewed patch is required");
        if (Jobs.Any(j => j.Digest == digest && j.State is "queued" or "running")) throw new InvalidOperationException("Validation already active");
        if (Jobs.Count >= 100) throw new InvalidOperationException("Validation limit reached");
        Jobs.Add(new() { Digest = digest, PatchDigest = Hash(Current.Patch), Commit = Commit }); Event(actor, "queued", digest);
    }
    public (ValidationJob Job, string Lease)? Claim()
    {
        var job = Jobs.LastOrDefault(j => j.Digest == Current.Digest && j.State == "queued");
        if (job is null || Current.Decision != "approved") return null;
        var lease = RandomToken(); job.State = "running"; job.ClaimedAt = DateTimeOffset.UtcNow; job.LeaseHash = Hash(lease);
        Event("runner", "running", job.Digest); return (job, lease);
    }
    public void Complete(Guid id, string lease, ValidationResult result)
    {
        var job = Jobs.SingleOrDefault(j => j.Id == id) ?? throw new InvalidOperationException("Unknown validation job");
        if (job.State != "running" || job.LeaseHash != Hash(lease) || job.ClaimedAt < DateTimeOffset.UtcNow.AddMinutes(-30))
            throw new InvalidOperationException("Invalid or expired runner lease");
        if (result.Digest != job.Digest || result.PatchDigest != job.PatchDigest || result.Commit != job.Commit)
            throw new InvalidOperationException("Validation does not match reviewed content");
        if ((result.Before.ExitCode is null && !result.Before.TimedOut) || (result.After.ExitCode is null && !result.After.TimedOut))
            throw new InvalidOperationException("Missing test exit status");
        job.Result = result; job.LeaseHash = null;
        job.State = Current.Digest != job.Digest || Current.Decision != "approved" ? "superseded" :
            !result.Before.TimedOut && result.Before.ExitCode != 0 && !result.After.TimedOut && result.After.ExitCode == 0 ? "validated" : "failed";
        Event("runner", job.State, job.Digest);
    }
    public void Cancel(Guid id, string actor)
    {
        var job = Jobs.SingleOrDefault(j => j.Id == id) ?? throw new InvalidOperationException("Unknown validation job");
        if (job.State is not ("queued" or "running")) throw new InvalidOperationException("Validation already finished");
        job.State = "cancelled"; job.LeaseHash = null; Event(actor, "cancelled", job.Digest);
    }
    public void RequireDigest(string digest)
    {
        if (Revisions.Count == 0 || Current.Digest != digest) throw new InvalidOperationException("Revision changed; reload before reviewing");
    }
    public void RequireApproved(string digest)
    {
        RequireDigest(digest); if (Current.Decision != "approved") throw new InvalidOperationException("Approve this revision first");
    }
    public void Event(string actor, string action, string digest) => Events.Add(new(DateTimeOffset.UtcNow, actor, action, digest));
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string RandomToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public static string RepositoryName(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[A-Za-z0-9_.-]{1,100}$") && !value.Split('/').Any(p => p is "." or "..")
        ? value : throw new InvalidOperationException("Invalid repository name");
}
