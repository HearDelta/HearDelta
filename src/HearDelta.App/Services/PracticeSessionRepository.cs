using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class PracticeSessionRepository : IPracticeSessionRepository, IRepositoryReadDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DatabaseConnectionFactory connections;
    public IReadOnlyList<RepositoryReadError> LastReadErrors { get; private set; } = [];

    public PracticeSessionRepository(DatabaseConnectionFactory connections)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public IReadOnlyList<PracticeSession> LoadAll()
        => LoadWhere(null);

    public IReadOnlyList<PracticeSession> LoadForPerson(Guid personId)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        return LoadWhere(personId);
    }

    private IReadOnlyList<PracticeSession> LoadWhere(Guid? personId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = personId is null
            ? "SELECT id, payload_json FROM practice_sessions ORDER BY started_at DESC"
            : "SELECT id, payload_json FROM practice_sessions WHERE person_id=$personId ORDER BY started_at DESC";
        if (personId is not null) command.Parameters.AddWithValue("$personId", personId.Value.ToString("D"));
        using var reader = command.ExecuteReader(); var sessions = new List<PracticeSession>();
        var errors = new List<RepositoryReadError>();
        while (reader.Read())
        {
            try { sessions.Add(Deserialize(reader.GetString(1))); }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            { errors.Add(new RepositoryReadError(reader.GetString(0), $"Datensatz ist unlesbar: {exception.Message}")); }
        }
        LastReadErrors = errors;
        return sessions;
    }

    public PracticeSession? Load(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM practice_sessions WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return command.ExecuteScalar() is string payload ? Deserialize(payload) : null;
    }

    public void Save(Guid personId, PracticeSession session)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Eine Person ist erforderlich.", nameof(personId));
        var errors = PracticeSessionRules.Validate(session);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(session));
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO practice_sessions (id, person_id, started_at, completed_at, aborted_at, payload_json, updated_at)
            VALUES ($id, $personId, $startedAt, $completedAt, $abortedAt, $payload, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET person_id=excluded.person_id, completed_at=excluded.completed_at, aborted_at=excluded.aborted_at,
            payload_json=excluded.payload_json, updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", session.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$abortedAt", session.AbortedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open() => connections.Open();
    private static PracticeSession Deserialize(string payload) => JsonSerializer.Deserialize<PracticeSession>(payload, JsonOptions)
        ?? throw new InvalidDataException("Das gespeicherte Übungsprotokoll ist leer oder unlesbar.");
}
