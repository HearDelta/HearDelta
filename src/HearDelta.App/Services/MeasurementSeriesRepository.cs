using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class MeasurementSeriesRepository : IMeasurementSeriesRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DatabaseConnectionFactory connections;

    public MeasurementSeriesRepository(DatabaseConnectionFactory connections)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public MeasurementSeriesState? LoadActive(Guid personId)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json, current_round_index FROM measurement_series WHERE person_id = $personId AND completed_at IS NULL ORDER BY updated_at DESC LIMIT 1";
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var plan = JsonSerializer.Deserialize<MeasurementSeriesPlan>(reader.GetString(0), JsonOptions)
            ?? throw new InvalidDataException("Der gespeicherte Serienplan ist leer oder unlesbar.");
        var currentRoundIndex = reader.GetInt32(1);
        Validate(plan, currentRoundIndex);
        return new MeasurementSeriesState(plan, currentRoundIndex);
    }

    public void Save(Guid personId, MeasurementSeriesState state)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        ArgumentNullException.ThrowIfNull(state);
        Validate(state.Plan, state.CurrentRoundIndex);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO measurement_series (id, person_id, payload_json, current_round_index, created_at, updated_at, completed_at)
            VALUES ($id, $personId, $payload, $currentRoundIndex, $now, $now, NULL)
            ON CONFLICT(id) DO UPDATE SET
                payload_json = excluded.payload_json,
                current_round_index = excluded.current_round_index,
                updated_at = excluded.updated_at,
                completed_at = NULL;
            """;
        command.Parameters.AddWithValue("$id", state.Plan.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(state.Plan, JsonOptions));
        command.Parameters.AddWithValue("$currentRoundIndex", state.CurrentRoundIndex);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Complete(Guid id) => SetCompletedAt(id, DateTimeOffset.UtcNow);

    public void Delete(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM measurement_series WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open() => connections.Open();

    private void SetCompletedAt(Guid id, DateTimeOffset completedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE measurement_series SET completed_at = $completedAt, updated_at = $completedAt WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$completedAt", completedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void Validate(MeasurementSeriesPlan plan, int currentRoundIndex)
    {
        if (plan.Version != 1 || plan.Id == Guid.Empty || plan.Rounds.Count < 2)
            throw new ArgumentException("Der Serienplan ist unvollständig oder wird nicht unterstützt.");
        if (currentRoundIndex < 0 || currentRoundIndex >= plan.Rounds.Count)
            throw new ArgumentOutOfRangeException(nameof(currentRoundIndex));
    }
}

public sealed class InMemoryMeasurementSeriesRepository : IMeasurementSeriesRepository
{
    private readonly Dictionary<Guid, MeasurementSeriesState> active = [];

    public MeasurementSeriesState? LoadActive(Guid personId) => active.GetValueOrDefault(personId);

    public void Save(Guid personId, MeasurementSeriesState state) => active[personId] = state;

    public void Complete(Guid id) => Remove(id);

    public void Delete(Guid id) => Remove(id);

    private void Remove(Guid id)
    {
        foreach (var personId in active.Where(entry => entry.Value.Plan.Id == id).Select(entry => entry.Key).ToArray())
            active.Remove(personId);
    }
}
