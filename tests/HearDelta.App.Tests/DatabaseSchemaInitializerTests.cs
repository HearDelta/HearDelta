using Microsoft.Data.Sqlite;
using HearDelta.App.Services;

namespace HearDelta.App.Tests;

public sealed class DatabaseSchemaInitializerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"heardelta-schema-{Guid.NewGuid():N}");

    [Fact]
    public void FreshDatabaseReceivesOnlyCurrentSchema()
    {
        var connections = CreateConnections();
        new DatabaseSchemaInitializer(connections).Initialize();

        using var connection = connections.Open();
        Assert.Equal(DatabaseSchemaInitializer.CurrentSchemaGeneration, ScalarText(connection,
            "SELECT value FROM application_metadata WHERE key='schema_generation';"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM pragma_table_info('persons') WHERE name='date_of_birth';"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM pragma_table_info('measurement_sessions') WHERE name='person_id' AND [notnull]=1;"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM pragma_table_info('practice_sessions') WHERE name='person_id' AND [notnull]=1;"));
        Assert.Equal(1L, Scalar(connection,
            "SELECT COUNT(*) FROM pragma_table_info('hearing_threshold_sessions') WHERE name='person_id' AND [notnull]=1;"));
        Assert.Equal(0L, Scalar(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('assessment_sessions', 'assessment_tests', 'assessment_test_contexts');"));
        Assert.Equal(0L, Scalar(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('schema_migrations', 'legacy_assignment_audit');"));
    }

    [Fact]
    public void CurrentSchemaInitializationIsIdempotent()
    {
        var connections = CreateConnections();
        var initializer = new DatabaseSchemaInitializer(connections);
        initializer.Initialize();
        initializer.Initialize();
    }

    [Fact]
    public void ExistingUnmarkedDatabaseIsBackedUpUnchangedAndReplaced()
    {
        var connections = CreateConnections();
        Execute(connections, "CREATE TABLE old_measurements(id TEXT PRIMARY KEY); INSERT INTO old_measurements VALUES('keep');");

        AssertBackedUpAndReplaced(connections);
    }

    [Fact]
    public void ExistingDatabaseWithOutdatedGenerationIsBackedUpUnchangedAndReplaced()
    {
        var connections = CreateConnections();
        Execute(connections, """
            CREATE TABLE application_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO application_metadata(key, value) VALUES('schema_generation', 'development-outdated');
            CREATE TABLE old_measurements(id TEXT PRIMARY KEY); INSERT INTO old_measurements VALUES('keep');
            """);

        AssertBackedUpAndReplaced(connections);
    }

    private void AssertBackedUpAndReplaced(DatabaseConnectionFactory connections)
    {
        var initializer = new DatabaseSchemaInitializer(connections);
        initializer.Initialize();

        Assert.NotNull(initializer.BackupPath);
        Assert.Matches(@"^test\.backup-\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.db$", Path.GetFileName(initializer.BackupPath));
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = initializer.BackupPath, Pooling = false }.ToString()))
        {
            backup.Open();
            Assert.Equal(1L, Scalar(backup, "SELECT COUNT(*) FROM old_measurements WHERE id='keep';"));
        }

        using var current = connections.Open();
        Assert.Equal(DatabaseSchemaInitializer.CurrentSchemaGeneration, ScalarText(current,
            "SELECT value FROM application_metadata WHERE key='schema_generation';"));
        Assert.Equal(0L, Scalar(current, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='old_measurements';"));
    }

    [Fact]
    public void BackupNoticeNamesBothPaths()
    {
        var notice = DatabaseSchemaInitializer.BuildBackupNotice(@"C:\db\heardelta.db", @"C:\db\heardelta.backup-2026-09-30_09-15-42.db");

        Assert.Contains(@"C:\db\heardelta.backup-2026-09-30_09-15-42.db", notice);
        Assert.Contains(@"C:\db\heardelta.db", notice);
    }

    [Fact]
    public void CurrentDatabaseIsNotBackedUp()
    {
        var connections = CreateConnections();
        new DatabaseSchemaInitializer(connections).Initialize();

        var initializer = new DatabaseSchemaInitializer(connections);
        initializer.Initialize();

        Assert.Null(initializer.BackupPath);
        Assert.Single(Directory.GetFiles(directory, "*.db"));
    }

    private static void Execute(DatabaseConnectionFactory connections, string sql)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private DatabaseConnectionFactory CreateConnections()
    {
        Directory.CreateDirectory(directory);
        return new DatabaseConnectionFactory(Path.Combine(directory, "test.db"));
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
