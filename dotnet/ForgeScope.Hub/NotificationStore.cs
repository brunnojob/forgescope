using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace ForgeScope.Hub;

public sealed partial class Store
{
    public async Task Watch(string owner, string repository)
    {
        repository = Investigation.RepositoryName(repository).ToLowerInvariant();
        await using var connection = await database.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext($1))", connection, tx))
        { guard.Parameters.AddWithValue(owner); await guard.ExecuteNonQueryAsync(); }
        await using var command = new NpgsqlCommand("INSERT INTO forgescope.repo_watches(owner_id,repository) SELECT $1,$2 WHERE (SELECT count(*) FROM forgescope.repo_watches WHERE owner_id=$1)<20 ON CONFLICT DO NOTHING", connection, tx);
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(repository); await command.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }
    public async Task Unwatch(string owner, string repository)
    {
        await using var command = database.CreateCommand("DELETE FROM forgescope.repo_watches WHERE owner_id=$1 AND repository=$2");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(repository.ToLowerInvariant()); await command.ExecuteNonQueryAsync();
    }
    public async Task<List<RepoWatch>> Watches(string owner)
    {
        await using var command = database.CreateCommand("SELECT repository,checked_at,error FROM forgescope.repo_watches WHERE owner_id=$1 ORDER BY repository");
        command.Parameters.AddWithValue(owner); await using var reader = await command.ExecuteReaderAsync(); var result = new List<RepoWatch>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2)));
        return result;
    }
    public async Task<bool> LeaseWatch(string owner, string repo)
    {
        await using var command = database.CreateCommand("UPDATE forgescope.repo_watches SET next_poll=now()+interval '2 minutes' WHERE owner_id=$1 AND repository=$2 AND next_poll<=now() RETURNING repository");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(repo.ToLowerInvariant()); return await command.ExecuteScalarAsync() is not null;
    }
    public async Task WatchResult(string owner, string repo, string error)
    {
        await using var command = database.CreateCommand("UPDATE forgescope.repo_watches SET checked_at=now(),error=$3 WHERE owner_id=$1 AND repository=$2");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(repo.ToLowerInvariant()); command.Parameters.AddWithValue(error); await command.ExecuteNonQueryAsync();
    }
    public async Task<List<(string Owner, string Repository, string Token)>> PollWatches()
    {
        await using var command = database.CreateCommand("SELECT w.owner_id,w.repository,s.protected_token FROM forgescope.repo_watches w JOIN LATERAL (SELECT protected_token FROM forgescope.sessions s WHERE s.user_id=w.owner_id AND s.expires_at>now() ORDER BY expires_at DESC LIMIT 1) s ON true WHERE w.next_poll<=now() ORDER BY w.next_poll LIMIT 10");
        await using var reader = await command.ExecuteReaderAsync(); var result = new List<(string, string, string)>();
        while (await reader.ReadAsync()) result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2))); return result;
    }
    public async Task<bool> SeenRun(string owner, string repo, long run, int attempt)
    {
        await using var command = database.CreateCommand("SELECT 1 FROM forgescope.monitored_runs WHERE owner_id=$1 AND repository=$2 AND run_id=$3 AND attempt=$4");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(repo); command.Parameters.AddWithValue(run); command.Parameters.AddWithValue(attempt);
        return await command.ExecuteScalarAsync() is not null;
    }
    public async Task SaveRunAlerts(string owner, string repo, long run, int attempt, List<Alert> alerts)
    {
        await using var connection = await database.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
        foreach (var alert in alerts)
        {
            await using var command = new NpgsqlCommand("INSERT INTO forgescope.notifications(owner_id,key,repository,document) VALUES ($1,$2,$3,$4) ON CONFLICT DO NOTHING", connection, tx);
            command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(alert.Key); command.Parameters.AddWithValue(repo);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(alert)); await command.ExecuteNonQueryAsync();
        }
        await using var seen = new NpgsqlCommand("INSERT INTO forgescope.monitored_runs VALUES ($1,$2,$3,$4) ON CONFLICT DO NOTHING", connection, tx);
        seen.Parameters.AddWithValue(owner); seen.Parameters.AddWithValue(repo); seen.Parameters.AddWithValue(run); seen.Parameters.AddWithValue(attempt); await seen.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }
    public async Task<List<Alert>> Notifications(string owner)
    {
        await using var command = database.CreateCommand("SELECT document,is_read,saved,done FROM forgescope.notifications WHERE owner_id=$1 ORDER BY created_at DESC,key LIMIT 500");
        command.Parameters.AddWithValue(owner); await using var reader = await command.ExecuteReaderAsync(); var result = new List<Alert>();
        while (await reader.ReadAsync()) result.Add(JsonSerializer.Deserialize<Alert>(reader.GetString(0))! with { Read = reader.GetBoolean(1), Saved = reader.GetBoolean(2), Done = reader.GetBoolean(3) }); return result;
    }
    public async Task<bool> NotificationState(string owner, string key, string action)
    {
        string set = action switch { "read" => "is_read=true", "unread" => "is_read=false", "save" => "saved=true", "unsave" => "saved=false", "done" => "done=true,is_read=true", "reopen" => "done=false", _ => throw new InvalidOperationException("Invalid notification action") };
        await using var command = database.CreateCommand($"UPDATE forgescope.notifications SET {set} WHERE owner_id=$1 AND key=$2");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(key); return await command.ExecuteNonQueryAsync() == 1;
    }
}
