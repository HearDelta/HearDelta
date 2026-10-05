using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class ProfileRepository
{
    private readonly DatabaseConnectionFactory connections;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProfileRepository(DatabaseConnectionFactory connections)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public IReadOnlyList<MeasurementProfile> LoadAll()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM measurement_profiles ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader();
        var result = new List<MeasurementProfile>();
        while (reader.Read())
        {
            var profile = JsonSerializer.Deserialize<MeasurementProfile>(reader.GetString(0), JsonOptions);
            if (profile is not null) result.Add(profile);
        }
        return result;
    }

    public void Save(MeasurementProfile profile)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO measurement_profiles (id, name, payload_json, updated_at)
            VALUES ($id, $name, $payload, $updated)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                payload_json = excluded.payload_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(profile, JsonOptions));
        command.Parameters.AddWithValue("$updated", profile.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }
}
