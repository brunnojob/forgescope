using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace ForgeScope.Hub;

public sealed record HubSession(string Id, string UserId, string Login, string ProtectedToken, DateTimeOffset ExpiresAt);
public sealed partial class Store(NpgsqlDataSource database)
{
    public async Task Migrate()
    {
        using var stream = typeof(Store).Assembly.GetManifestResourceStream("ForgeScope.Hub.schema.sql")!;
        using var reader = new StreamReader(stream);
        await using var command = database.CreateCommand(await reader.ReadToEndAsync()); await command.ExecuteNonQueryAsync();
    }
    public async Task SaveSession(HubSession session)
    {
        await using var command = database.CreateCommand("INSERT INTO forgescope.sessions VALUES ($1,$2,$3,$4,$5)");
        command.Parameters.AddWithValue(session.Id); command.Parameters.AddWithValue(session.UserId); command.Parameters.AddWithValue(session.Login);
        command.Parameters.AddWithValue(session.ProtectedToken); command.Parameters.AddWithValue(session.ExpiresAt); await command.ExecuteNonQueryAsync();
    }
    public async Task<HubSession?> Session(string id)
    {
        await using var command = database.CreateCommand("SELECT user_id,login,protected_token,expires_at FROM forgescope.sessions WHERE id=$1 AND expires_at>now()");
        command.Parameters.AddWithValue(id); await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)) : null;
    }
    public async Task Logout(string id)
    {
        await using var command = database.CreateCommand("DELETE FROM forgescope.sessions WHERE id=$1"); command.Parameters.AddWithValue(id); await command.ExecuteNonQueryAsync();
    }
    public async Task Create(Investigation value)
    {
        await using var command = database.CreateCommand("INSERT INTO forgescope.investigations(id,owner_id,repository,document) VALUES ($1,$2,$3,$4)");
        command.Parameters.AddWithValue(value.Id); command.Parameters.AddWithValue(value.OwnerId); command.Parameters.AddWithValue(value.Repository);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value)); await command.ExecuteNonQueryAsync();
    }
    public async Task<List<Investigation>> List(string owner)
    {
        await using var command = database.CreateCommand("SELECT document,version FROM forgescope.investigations WHERE owner_id=$1 ORDER BY updated_at DESC LIMIT 100");
        command.Parameters.AddWithValue(owner); await using var reader = await command.ExecuteReaderAsync(); var rows = new List<Investigation>();
        while (await reader.ReadAsync()) { var value = JsonSerializer.Deserialize<Investigation>(reader.GetString(0))!; value.Version = reader.GetInt32(1); rows.Add(value); }
        return rows;
    }
    public async Task<Investigation?> Get(string owner, Guid id)
    {
        await using var command = database.CreateCommand("SELECT document,version FROM forgescope.investigations WHERE owner_id=$1 AND id=$2");
        command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(id); await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var result = JsonSerializer.Deserialize<Investigation>(reader.GetString(0))!; result.Version = reader.GetInt32(1); return result;
    }
    // A row lock serializes review and runner transitions; the browser version catches stale forms.
    public async Task<Investigation> Update(string owner, Guid id, int? expected, Action<Investigation> change)
    {
        await using var connection = await database.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var read = new NpgsqlCommand("SELECT document,version FROM forgescope.investigations WHERE owner_id=$1 AND id=$2 FOR UPDATE", connection, transaction);
        read.Parameters.AddWithValue(owner); read.Parameters.AddWithValue(id);
        Investigation value; int version;
        await using (var reader = await read.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new KeyNotFoundException();
            value = JsonSerializer.Deserialize<Investigation>(reader.GetString(0))!; version = reader.GetInt32(1);
        }
        if (expected.HasValue && version != expected) throw new InvalidOperationException("Content changed; reload before saving");
        change(value); value.Version = version + 1; value.UpdatedAt = DateTimeOffset.UtcNow;
        await using var write = new NpgsqlCommand("UPDATE forgescope.investigations SET document=$1,version=$2,updated_at=now() WHERE owner_id=$3 AND id=$4", connection, transaction);
        write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value)); write.Parameters.AddWithValue(value.Version);
        write.Parameters.AddWithValue(owner); write.Parameters.AddWithValue(id); await write.ExecuteNonQueryAsync(); await transaction.CommitAsync(); return value;
    }
    public async Task<string> RunnerToken(string owner, Guid id)
    {
        if (await Get(owner, id) is null) throw new KeyNotFoundException();
        string token = Investigation.RandomToken();
        await using var command = database.CreateCommand("INSERT INTO forgescope.runner_tokens VALUES ($1,$2,$3,$4)");
        command.Parameters.AddWithValue(Investigation.Hash(token)); command.Parameters.AddWithValue(owner); command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddHours(1)); await command.ExecuteNonQueryAsync(); return token;
    }
    public async Task<(string Owner, Guid Investigation)?> Runner(string token)
    {
        if (token.Length != 64) return null;
        await using var command = database.CreateCommand("SELECT owner_id,investigation_id FROM forgescope.runner_tokens WHERE digest=$1 AND expires_at>now()");
        command.Parameters.AddWithValue(Investigation.Hash(token)); await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetGuid(1)) : null;
    }
}
