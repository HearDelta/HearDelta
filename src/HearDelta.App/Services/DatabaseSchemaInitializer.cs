using Microsoft.Data.Sqlite;
using System.IO;

namespace HearDelta.App.Services;

public sealed class DatabaseSchemaInitializer(DatabaseConnectionFactory connections)
{
    public const string CurrentSchemaGeneration = "development-2026-09-30-person-measurement-series";

    public void Initialize()
    {
        BackupPath = null;
        using (var connection = connections.Open())
        using (var transaction = connection.BeginTransaction())
        {
            var tables = LoadUserTables(connection, transaction);
            if (tables.Count > 0)
            {
                var dbSchema = tables.Contains("application_metadata")
                    ? ReadSchemaGeneration(connection, transaction)
                    : null;
                if (dbSchema == CurrentSchemaGeneration)
                {
                    CreateAdditiveTables(connection, transaction);
                    transaction.Commit();
                    return;
                }

                // Die Datei muss vor dem Umbenennen freigegeben sein; Pooling ist abgeschaltet.
                transaction.Rollback();
                connection.Close();
                BackupPath = BackUpDatabaseFiles(connections.DatabasePath);
            }
            else
            {
                CreateCurrentSchema(connection, transaction);
                transaction.Commit();
                return;
            }
        }

        using var freshConnection = connections.Open();
        using var freshTransaction = freshConnection.BeginTransaction();
        CreateCurrentSchema(freshConnection, freshTransaction);
        freshTransaction.Commit();
    }

    /// <summary>Pfad der beim letzten <see cref="Initialize"/> angelegten Sicherung, sonst null.</summary>
    public string? BackupPath { get; private set; }

    public static string BuildBackupNotice(string databasePath, string backupPath) =>
        "Die vorhandene Datenbank passte nicht zum aktuellen Datenbankschema dieser Version.\n\n" +
        $"Sie wurde unverändert gesichert unter:\n{backupPath}\n\n" +
        $"Unter {databasePath} wurde eine neue, leere Datenbank angelegt. " +
        "Bisherige Personen, Messprofile und Messungen erscheinen daher nicht mehr in der Anwendung; " +
        "sie sind in der Sicherungsdatei weiterhin vorhanden.";

    /// <summary>
    /// Benennt die Datenbank samt WAL-/SHM-Begleitdateien in
    /// <c>&lt;name&gt;.backup-yyyy-MM-dd_HH-mm-ss.db</c> (lokale Zeit) um. Inhalte werden nicht verändert.
    /// </summary>
    private static string BackUpDatabaseFiles(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(dbPath);
        var extension = Path.GetExtension(dbPath);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);

        var backupPath = Path.Combine(directory, $"{baseName}.backup-{stamp}{extension}");
        for (var suffix = 2; File.Exists(backupPath) || File.Exists(backupPath + "-wal") || File.Exists(backupPath + "-shm"); suffix++)
            backupPath = Path.Combine(directory, $"{baseName}.backup-{stamp}-{suffix}{extension}");

        try
        {
            File.Move(dbPath, backupPath);
            foreach (var sidecar in new[] { "-wal", "-shm" })
            {
                if (File.Exists(dbPath + sidecar))
                    File.Move(dbPath + sidecar, backupPath + sidecar);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Die vorhandene Datenbank '{dbPath}' entspricht nicht dem aktuellen Schema und konnte nicht nach '{backupPath}' gesichert werden: {ex.Message} " +
                "Lösche oder verschiebe sie manuell und starte die Anwendung anschließend erneut.", ex);
        }

        return backupPath;
    }

    private static HashSet<string> LoadUserTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using var reader = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static string? ReadSchemaGeneration(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM application_metadata WHERE key='schema_generation';";
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Vereinbarte additive Ergänzung (02.10.2026): Tabellen, die einer Datenbank der aktuellen Generation
    /// fehlen dürfen, werden nachträglich angelegt. Vorhandene Tabellen und Daten bleiben unberührt.
    /// </summary>
    private static void CreateAdditiveTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS measurement_annotations (
                measurement_id TEXT PRIMARY KEY, name TEXT NOT NULL, comment TEXT NULL, updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void CreateCurrentSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        CreateAdditiveTables(connection, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE application_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO application_metadata(key, value) VALUES('schema_generation', $schemaGeneration);
            CREATE TABLE persons (
                id TEXT PRIMARY KEY, display_name TEXT NOT NULL, notes TEXT NULL, date_of_birth TEXT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL, archived_at TEXT NULL
            );
            CREATE TABLE person_test_preferences (
                person_id TEXT PRIMARY KEY, preferred_hardware_profile_id TEXT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE TABLE person_hearing_aids (
                id TEXT PRIMARY KEY, person_id TEXT NOT NULL, manufacturer TEXT NOT NULL, model TEXT NOT NULL,
                display_name TEXT NOT NULL, ear INTEGER NOT NULL, archived_at TEXT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE TABLE listening_settings (
                id TEXT NOT NULL, person_id TEXT NOT NULL, revision INTEGER NOT NULL, name TEXT NOT NULL,
                ear INTEGER NOT NULL, condition INTEGER NOT NULL, hearing_aid_id TEXT NULL,
                program_name TEXT NULL, volume_state TEXT NULL, wearing_notes TEXT NULL, archived_at TEXT NULL,
                PRIMARY KEY(id, revision),
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT,
                FOREIGN KEY(hearing_aid_id) REFERENCES person_hearing_aids(id) ON DELETE RESTRICT
            );
            CREATE TABLE measurement_profiles (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, payload_json TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE TABLE measurement_sessions (
                id TEXT PRIMARY KEY, person_id TEXT NOT NULL, protocol_version INTEGER NOT NULL, started_at TEXT NOT NULL,
                completed_at TEXT NULL, aborted_at TEXT NULL, tested_ear INTEGER NOT NULL,
                hearing_aid_id TEXT NOT NULL, payload_json TEXT NOT NULL, updated_at TEXT NOT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE TABLE hearing_threshold_sessions (
                id TEXT PRIMARY KEY, person_id TEXT NOT NULL, protocol_version INTEGER NOT NULL, started_at TEXT NOT NULL,
                completed_at TEXT NULL, aborted_at TEXT NULL, tested_ear INTEGER NOT NULL,
                hearing_aid_condition INTEGER NOT NULL, hearing_aid_id TEXT NULL, payload_json TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE TABLE practice_sessions (
                id TEXT PRIMARY KEY, person_id TEXT NOT NULL, started_at TEXT NOT NULL, completed_at TEXT NULL, aborted_at TEXT NULL,
                payload_json TEXT NOT NULL, updated_at TEXT NOT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE TABLE measurement_series (
                id TEXT PRIMARY KEY, person_id TEXT NOT NULL, payload_json TEXT NOT NULL, current_round_index INTEGER NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT NULL,
                FOREIGN KEY(person_id) REFERENCES persons(id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_hearing_aids_person ON person_hearing_aids(person_id, archived_at);
            CREATE INDEX ix_listening_settings_person ON listening_settings(person_id, archived_at, name);
            CREATE INDEX ix_measurement_sessions_person_started ON measurement_sessions(person_id, started_at DESC);
            CREATE INDEX ix_measurement_sessions_started_at ON measurement_sessions(started_at DESC);
            CREATE INDEX ix_measurement_sessions_device_ear ON measurement_sessions(hearing_aid_id, tested_ear, started_at DESC);
            CREATE INDEX ix_hearing_threshold_sessions_person_started ON hearing_threshold_sessions(person_id, started_at DESC);
            CREATE INDEX ix_hearing_threshold_sessions_started_at ON hearing_threshold_sessions(started_at DESC);
            CREATE INDEX ix_hearing_threshold_sessions_condition_ear ON hearing_threshold_sessions(hearing_aid_condition, tested_ear, started_at DESC);
            CREATE INDEX ix_practice_sessions_person_started ON practice_sessions(person_id, started_at DESC);
            CREATE INDEX ix_practice_sessions_started_at ON practice_sessions(started_at DESC);
            CREATE INDEX ix_measurement_series_person_active ON measurement_series(person_id, completed_at, updated_at DESC);
            """;
        command.Parameters.AddWithValue("$schemaGeneration", CurrentSchemaGeneration);
        command.ExecuteNonQuery();
    }
}
