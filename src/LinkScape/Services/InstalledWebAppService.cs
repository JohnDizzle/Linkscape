using LinkScape.Models;
using Microsoft.Data.Sqlite;

namespace LinkScape.Services;

public static class InstalledWebAppService
{
    private static readonly string DbConnectionString =
        LinkScapeCachePaths.GetDatabaseConnectionString("webapps.db");

    public static void EnsureDatabase()
    {
        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS InstalledWebApps
            (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                ShortName TEXT,
                Origin TEXT NOT NULL,
                StartUrl TEXT NOT NULL,
                Scope TEXT NOT NULL,
                ManifestUrl TEXT NOT NULL,
                IconUrl TEXT,
                LocalIconPath TEXT,
                ThemeColor TEXT,
                DisplayMode TEXT NOT NULL,
                InstalledAt TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_InstalledWebApps_ManifestUrl
            ON InstalledWebApps(ManifestUrl);
            """;

        command.ExecuteNonQuery();
    }

    public static InstalledWebApp Install(InstallableWebApp candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var startUri = new Uri(candidate.StartUrl);
        var app = new InstalledWebApp
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = candidate.Name,
            ShortName = candidate.ShortName,
            Origin = $"{startUri.Scheme}://{startUri.Authority}",
            StartUrl = candidate.StartUrl,
            Scope = candidate.Scope,
            ManifestUrl = candidate.ManifestUrl,
            IconUrl = candidate.IconUrl,
            ThemeColor = candidate.ThemeColor,
            DisplayMode = candidate.DisplayMode,
            InstalledAt = DateTimeOffset.UtcNow
        };

        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO InstalledWebApps
            (Id, Name, ShortName, Origin, StartUrl, Scope, ManifestUrl, IconUrl,
             LocalIconPath, ThemeColor, DisplayMode, InstalledAt)
            VALUES
            ($id, $name, $shortName, $origin, $startUrl, $scope, $manifestUrl,
             $iconUrl, $localIconPath, $themeColor, $displayMode, $installedAt);
            """;

        command.Parameters.AddWithValue("$id", app.Id);
        command.Parameters.AddWithValue("$name", app.Name);
        command.Parameters.AddWithValue("$shortName", (object?)app.ShortName ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin", app.Origin);
        command.Parameters.AddWithValue("$startUrl", app.StartUrl);
        command.Parameters.AddWithValue("$scope", app.Scope);
        command.Parameters.AddWithValue("$manifestUrl", app.ManifestUrl ?? string.Empty);
        command.Parameters.AddWithValue("$iconUrl", (object?)app.IconUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$localIconPath", DBNull.Value);
        command.Parameters.AddWithValue("$themeColor", (object?)app.ThemeColor ?? DBNull.Value);
        command.Parameters.AddWithValue("$displayMode", app.DisplayMode);
        command.Parameters.AddWithValue("$installedAt", app.InstalledAt.ToString("O"));
        command.ExecuteNonQuery();

        return app;
    }

    public static bool IsInstalled(string manifestUrl)
    {
        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM InstalledWebApps
                WHERE ManifestUrl = $manifestUrl
            );
            """;
        command.Parameters.AddWithValue("$manifestUrl", manifestUrl);

        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    public static InstalledWebApp[] GetAll()
    {
        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, ShortName, Origin, StartUrl, Scope, ManifestUrl,
                   IconUrl, LocalIconPath, ThemeColor, DisplayMode, InstalledAt
            FROM InstalledWebApps
            ORDER BY Name COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var results = new List<InstalledWebApp>();
        while (reader.Read())
        {
            results.Add(ReadApp(reader));
        }

        return [.. results];
    }

    public static InstalledWebApp? Get(string id)
    {
        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, ShortName, Origin, StartUrl, Scope, ManifestUrl,
                   IconUrl, LocalIconPath, ThemeColor, DisplayMode, InstalledAt
            FROM InstalledWebApps
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadApp(reader) : null;
    }

    public static bool Uninstall(string id)
    {
        using var connection = new SqliteConnection(DbConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM InstalledWebApps WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private static InstalledWebApp ReadApp(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        ShortName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Origin = reader.GetString(3),
        StartUrl = reader.GetString(4),
        Scope = reader.GetString(5),
        ManifestUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
        IconUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
        LocalIconPath = reader.IsDBNull(8) ? null : reader.GetString(8),
        ThemeColor = reader.IsDBNull(9) ? null : reader.GetString(9),
        DisplayMode = reader.GetString(10),
        InstalledAt = DateTimeOffset.Parse(reader.GetString(11))
    };
}
