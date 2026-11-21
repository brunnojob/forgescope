using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace ForgeScope.Hub;

// ASP.NET encrypts each key's secret material with the configured certificate before storage.
// Persisting the key ring in PostgreSQL avoids ephemeral container files and a separate file share.
public sealed class PostgresKeys(NpgsqlDataSource database) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var command = database.CreateCommand("SELECT xml FROM forgescope.data_protection_keys ORDER BY created_at,id");
        using var reader = command.ExecuteReader(); var keys = new List<XElement>();
        while (reader.Read()) keys.Add(XElement.Parse(reader.GetString(0))); return keys;
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        using var command = database.CreateCommand("INSERT INTO forgescope.data_protection_keys(id,xml) VALUES ($1,$2)");
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(element.ToString(SaveOptions.DisableFormatting)); command.ExecuteNonQuery();
    }
}
