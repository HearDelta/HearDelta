using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class MeasurementSessionRepository : IMeasurementSessionRepository, IRepositoryReadDiagnostics
{
    private readonly DatabaseConnectionFactory connections;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<RepositoryReadError> LastReadErrors { get; private set; } = [];

    public MeasurementSessionRepository(DatabaseConnectionFactory connections)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public IReadOnlyList<PairedMeasurementSession> LoadAll()
        => LoadWhere(null);

    public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        return LoadWhere(personId);
    }

    private IReadOnlyList<PairedMeasurementSession> LoadWhere(Guid? personId)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = personId is null
            ? "SELECT id, payload_json FROM measurement_sessions ORDER BY started_at DESC"
            : "SELECT id, payload_json FROM measurement_sessions WHERE person_id=$personId ORDER BY started_at DESC";
        if (personId is not null) command.Parameters.AddWithValue("$personId", personId.Value.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = new List<PairedMeasurementSession>();
        var errors = new List<RepositoryReadError>();
        while (reader.Read())
        {
            try
            {
                result.Add(Deserialize(reader.GetString(1)));
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                errors.Add(new RepositoryReadError(reader.GetString(0), $"Datensatz ist unlesbar: {exception.Message}"));
            }
        }
        LastReadErrors = errors;
        return result;
    }

    public PairedMeasurementSession? Load(Guid id)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM measurement_sessions WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var payload = command.ExecuteScalar() as string;
        return payload is null ? null : Deserialize(payload);
    }

    public void Save(Guid personId, PairedMeasurementSession session)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        var errors = PairedMeasurementSessionRules.Validate(session);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(session));

        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO measurement_sessions (
                id, person_id, protocol_version, started_at, completed_at, aborted_at, tested_ear,
                hearing_aid_id, payload_json, updated_at)
            VALUES (
                $id, $personId, $protocolVersion, $startedAt, $completedAt, $abortedAt, $testedEar,
                $hearingAidId, $payload, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                person_id = excluded.person_id,
                protocol_version = excluded.protocol_version,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                aborted_at = excluded.aborted_at,
                tested_ear = excluded.tested_ear,
                hearing_aid_id = excluded.hearing_aid_id,
                payload_json = excluded.payload_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        command.Parameters.AddWithValue("$protocolVersion", session.ProtocolVersion);
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", session.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$abortedAt", session.AbortedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$testedEar", (int)session.Ear);
        command.Parameters.AddWithValue("$hearingAidId", session.HearingAid.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM measurement_sessions WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static PairedMeasurementSession Deserialize(string payload)
    {
        var session = JsonSerializer.Deserialize<PairedMeasurementSession>(payload, JsonOptions)
            ?? throw new InvalidDataException("Das gespeicherte Messprotokoll ist leer oder unlesbar.");
        var errors = PairedMeasurementSessionRules.Validate(session);
        if (errors.Count > 0)
            throw new InvalidDataException($"Das gespeicherte Messprotokoll ist ungültig: {string.Join(" ", errors)}");
        return session;
    }

}
