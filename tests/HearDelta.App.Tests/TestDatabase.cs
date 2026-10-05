using HearDelta.App.Services;

namespace HearDelta.App.Tests;

internal static class TestDatabase
{
    public static DatabaseConnectionFactory Initialize(string databasePath)
    {
        var connections = new DatabaseConnectionFactory(databasePath);
        new DatabaseSchemaInitializer(connections).Initialize();
        return connections;
    }
}
