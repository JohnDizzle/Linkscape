using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

[assembly: DoNotParallelize]

internal static class TestCacheScope
{
    public static string RootPath { get; } = Path.Combine(Path.GetTempPath(), "LinkScape.Tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("LINKSCAPE_CACHE_DIRECTORY", RootPath, EnvironmentVariableTarget.Process);
        Directory.CreateDirectory(RootPath);
    }

    public static void Reset()
    {
        Directory.CreateDirectory(RootPath);
        Environment.SetEnvironmentVariable("LINKSCAPE_CACHE_DIRECTORY", RootPath, EnvironmentVariableTarget.Process);

        ResetDatabase("tabs.db", "DROP TABLE IF EXISTS KeyValue;");
        ResetDatabase("history.db", "DROP TABLE IF EXISTS HistoryArchiveItems; DROP TABLE IF EXISTS HistoryItems;");
        ResetDatabase("favorites.db", "DROP TABLE IF EXISTS Favorites;");
        ResetDatabase("settings.db", "DROP TABLE IF EXISTS Settings;");
        ResetDatabase("tabCollections.db", "DROP TABLE IF EXISTS TabCollectionItems; DROP TABLE IF EXISTS TabCollections;");
    }

    private static void ResetDatabase(string databaseFileName, string resetSql)
    {
        var connectionString = LinkScapeCachePaths.GetDatabaseConnectionString(databaseFileName);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = resetSql;
        command.ExecuteNonQuery();
    }
}
