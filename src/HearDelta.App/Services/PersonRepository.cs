using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class PersonRepository(DatabaseConnectionFactory connections)
{
    public IReadOnlyList<PersonProfile> LoadAll(bool includeArchived = false)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, display_name, notes, created_at, updated_at, archived_at, date_of_birth FROM persons {(includeArchived ? string.Empty : "WHERE archived_at IS NULL")} ORDER BY display_name COLLATE NOCASE, created_at;";
        using var reader = command.ExecuteReader();
        var result = new List<PersonProfile>();
        while (reader.Read())
            result.Add(ReadPerson(reader));
        return result;
    }

    public PersonProfile? Load(Guid id)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, display_name, notes, created_at, updated_at, archived_at, date_of_birth FROM persons WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPerson(reader) : null;
    }

    public void Save(PersonProfile person)
    {
        var errors = PersonProfileRules.Validate(person);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(person));
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        Save(person, connection, transaction);
        transaction.Commit();
    }

    internal static void Save(PersonProfile person, SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO persons(id, display_name, notes, created_at, updated_at, archived_at, date_of_birth)
            VALUES($id, $name, $notes, $createdAt, $updatedAt, $archivedAt, $dateOfBirth)
            ON CONFLICT(id) DO UPDATE SET display_name=excluded.display_name, notes=excluded.notes,
                updated_at=excluded.updated_at, archived_at=excluded.archived_at,
                date_of_birth=excluded.date_of_birth;
            """;
        command.Parameters.AddWithValue("$id", person.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", person.DisplayName.Trim());
        command.Parameters.AddWithValue("$notes", person.Notes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", person.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", person.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$archivedAt", person.ArchivedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$dateOfBirth", person.DateOfBirth?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void SaveHearingAid(PersonHearingAid hearingAid)
    {
        var errors = PersonProfileRules.Validate(hearingAid);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(hearingAid));
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO person_hearing_aids(id, person_id, manufacturer, model, display_name, ear, archived_at)
            VALUES($id, $personId, $manufacturer, $model, $name, $ear, $archivedAt)
            ON CONFLICT(id) DO UPDATE SET manufacturer=excluded.manufacturer, model=excluded.model,
                display_name=excluded.display_name, ear=excluded.ear, archived_at=excluded.archived_at;
            """;
        command.Parameters.AddWithValue("$id", hearingAid.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", hearingAid.PersonId.ToString("D"));
        command.Parameters.AddWithValue("$manufacturer", hearingAid.Manufacturer.Trim());
        command.Parameters.AddWithValue("$model", hearingAid.Model.Trim());
        command.Parameters.AddWithValue("$name", hearingAid.DisplayName.Trim());
        command.Parameters.AddWithValue("$ear", (int)hearingAid.Ear);
        command.Parameters.AddWithValue("$archivedAt", hearingAid.ArchivedAt?.ToString("O") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PersonHearingAid> LoadHearingAids(Guid personId, bool includeArchived = false)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, person_id, manufacturer, model, display_name, ear, archived_at FROM person_hearing_aids WHERE person_id=$personId {(includeArchived ? string.Empty : "AND archived_at IS NULL")} ORDER BY display_name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = new List<PersonHearingAid>();
        while (reader.Read())
            result.Add(new PersonHearingAid(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), (TestedEar)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))));
        return result;
    }

    private static PersonProfile ReadPerson(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4)),
        reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : DateOnly.ParseExact(reader.GetString(6), "yyyy-MM-dd"));
}
