using System.IO;
using Microsoft.Data.Sqlite;

namespace HearDelta.App.Services;

public sealed class DatabaseConnectionFactory
{
    public DatabaseConnectionFactory(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? DefaultDatabasePath);
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new ArgumentException("Der Datenbankpfad hat kein gültiges Verzeichnis.", nameof(databasePath));
        Directory.CreateDirectory(directory);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        }.ToString();
    }

    /// <summary>Umgebungsvariable für einen abweichenden Datenbankpfad, z. B. für Demo- oder Testdaten.</summary>
    public const string DatabasePathVariable = "HEARDELTA_DATABASE";

    public static string DefaultDatabasePath =>
        Environment.GetEnvironmentVariable(DatabasePathVariable) is { Length: > 0 } overridePath
            ? overridePath
            : StandardDatabasePath;

    public static string StandardDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HearDelta",
        "heardelta.db");

    public string DatabasePath { get; }
    public string ConnectionString { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
}
