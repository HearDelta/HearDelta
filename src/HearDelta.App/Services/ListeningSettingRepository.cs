using Microsoft.Data.Sqlite;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class ListeningSettingRepository(DatabaseConnectionFactory connections)
{
    public void Save(ListeningSetting setting, PersonHearingAid? hearingAid)
    {
        var errors = ListeningSettingRules.Validate(setting, hearingAid);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(setting));
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO listening_settings(id, person_id, revision, name, ear, condition,
                hearing_aid_id, program_name, volume_state, wearing_notes, archived_at)
            VALUES($id, $personId, $revision, $name, $ear, $condition, $hearingAidId,
                $programName, $volumeState, $wearingNotes, $archivedAt);
            """;
        command.Parameters.AddWithValue("$id", setting.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", setting.PersonId.ToString("D"));
        command.Parameters.AddWithValue("$revision", setting.Revision);
        command.Parameters.AddWithValue("$name", setting.Name.Trim());
        command.Parameters.AddWithValue("$ear", (int)setting.Ear);
        command.Parameters.AddWithValue("$condition", (int)setting.Condition);
        command.Parameters.AddWithValue("$hearingAidId", setting.HearingAidId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$programName", setting.ProgramName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$volumeState", setting.VolumeState ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$wearingNotes", setting.WearingNotes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$archivedAt", setting.ArchivedAt?.ToString("O") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ListeningSetting> LoadCurrent(Guid personId, bool includeArchived = false)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.id, s.person_id, s.revision, s.name, s.ear, s.condition, s.hearing_aid_id,
                   s.program_name, s.volume_state, s.wearing_notes, s.archived_at
            FROM listening_settings s
            INNER JOIN (SELECT id, MAX(revision) revision FROM listening_settings GROUP BY id) latest
                ON latest.id=s.id AND latest.revision=s.revision
            WHERE s.person_id=$personId {(includeArchived ? string.Empty : "AND s.archived_at IS NULL")}
            ORDER BY s.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        using var reader = command.ExecuteReader();
        var result = new List<ListeningSetting>();
        while (reader.Read())
            result.Add(new ListeningSetting(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetInt32(2),
                reader.GetString(3), (TestedEar)reader.GetInt32(4), (HearingAidCondition)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10))));
        return result;
    }
}
