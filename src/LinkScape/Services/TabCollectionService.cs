using System.Globalization;
using LinkScape.Models;

public sealed record TabCollection(
    string Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

public sealed record TabCollectionItem(
    string Id,
    string CollectionId,
    string Url,
    string Title,
    int Order,
    DateTime CreatedAt,
    DateTime UpdatedAt) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

public static class TabCollectionService
{
    public const string StartupCollectionSettingKey = "browser.tabs.startupCollectionId";
    public const string StartupModeSettingKey = "browser.tabs.startupMode";
    public const string StartupModeCollection = "collection";

    private static readonly string DbConnectionString = LinkScapeCachePaths.GetDatabaseConnectionString("tabCollections.db");

    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TabCollections(
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_TabCollections_Name
            ON TabCollections(Name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS TabCollectionItems(
                Id TEXT PRIMARY KEY,
                CollectionId TEXT NOT NULL,
                Url TEXT NOT NULL,
                Title TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(CollectionId) REFERENCES TabCollections(Id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_TabCollectionItems_Collection_Url
            ON TabCollectionItems(CollectionId, Url);

            CREATE INDEX IF NOT EXISTS IX_TabCollectionItems_Collection_Order
            ON TabCollectionItems(CollectionId, SortOrder);
            """;
        cmd.ExecuteNonQuery();
    }

    public static TabCollection UpsertCollection(string name)
    {
        var safeName = NormalizeCollectionName(name);
        var now = DateTime.Now;
        var existing = GetCollectionByName(safeName);
        var id = existing?.Id ?? Guid.NewGuid().ToString("N");

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TabCollections(Id, Name, CreatedAt, UpdatedAt)
            VALUES($id, $name, $createdAt, $updatedAt)
            ON CONFLICT(Name) DO UPDATE SET
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", safeName);
        cmd.Parameters.AddWithValue("$createdAt", (existing?.CreatedAt ?? now).ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        cmd.ExecuteNonQuery();

        return GetCollectionByName(safeName)
            ?? new TabCollection(id, safeName, existing?.CreatedAt ?? now, now);
    }

    public static bool RenameCollection(string currentNameOrId, string nextName)
    {
        if (string.IsNullOrWhiteSpace(currentNameOrId))
        {
            return false;
        }

        var collection = GetCollection(currentNameOrId) ?? GetCollectionByName(currentNameOrId);
        if (collection is null)
        {
            return false;
        }

        var safeName = NormalizeCollectionName(nextName);

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE TabCollections
            SET Name = $name,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", collection.Id);
        cmd.Parameters.AddWithValue("$name", safeName);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public static TabCollectionItem AddOrUpdateItem(string collectionName, string url, string? title)
    {
        var normalizedUrl = StoredUrlNormalizer.Normalize(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            throw new ArgumentException("A valid collection item URL is required.", nameof(url));
        }

        var collection = UpsertCollection(collectionName);
        var existing = GetItems(collection.Id).FirstOrDefault(item => string.Equals(item.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
        var now = DateTime.Now;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? normalizedUrl : title.Trim();
        var nextOrder = existing?.Order ?? GetNextOrder(collection.Id);
        var id = existing?.Id ?? Guid.NewGuid().ToString("N");

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TabCollectionItems(Id, CollectionId, Url, Title, SortOrder, CreatedAt, UpdatedAt)
            VALUES($id, $collectionId, $url, $title, $sortOrder, $createdAt, $updatedAt)
            ON CONFLICT(CollectionId, Url) DO UPDATE SET
                Title = excluded.Title,
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$collectionId", collection.Id);
        cmd.Parameters.AddWithValue("$url", normalizedUrl);
        cmd.Parameters.AddWithValue("$title", safeTitle);
        cmd.Parameters.AddWithValue("$sortOrder", nextOrder);
        cmd.Parameters.AddWithValue("$createdAt", (existing?.CreatedAt ?? now).ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        cmd.ExecuteNonQuery();

        return GetItems(collection.Id).First(item => string.Equals(item.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
    }

    internal static TabCollectionItem AddOrUpdateItem(string collectionName, BrowserTab tab) =>
        AddOrUpdateItem(collectionName, tab.Url, string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title);

    public static bool RemoveItem(string collectionNameOrId, string url)
    {
        var collection = GetCollection(collectionNameOrId) ?? GetCollectionByName(collectionNameOrId);
        var normalizedUrl = StoredUrlNormalizer.Normalize(url);
        if (collection is null || string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return false;
        }

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM TabCollectionItems WHERE CollectionId = $collectionId AND Url = $url";
        cmd.Parameters.AddWithValue("$collectionId", collection.Id);
        cmd.Parameters.AddWithValue("$url", normalizedUrl);
        return cmd.ExecuteNonQuery() > 0;
    }

    public static void SetStartupCollection(string collectionNameOrId)
    {
        var collection = GetCollection(collectionNameOrId) ?? GetCollectionByName(collectionNameOrId);
        if (collection is null)
        {
            throw new InvalidOperationException($"Collection '{collectionNameOrId}' was not found.");
        }

        SettingsService.SetValue(StartupModeSettingKey, StartupModeCollection);
        SettingsService.SetValue(StartupCollectionSettingKey, collection.Id);
    }

    public static TabCollection? GetStartupCollection()
    {
        if (!string.Equals(SettingsService.GetValue(StartupModeSettingKey), StartupModeCollection, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var collectionId = SettingsService.GetValue(StartupCollectionSettingKey);
        return string.IsNullOrWhiteSpace(collectionId) ? null : GetCollection(collectionId);
    }

    public static IReadOnlyList<TabCollection> GetCollections()
    {
        return QueryCollections(
            """
            SELECT Id, Name, CreatedAt, UpdatedAt
            FROM TabCollections
            ORDER BY UpdatedAt DESC, Name COLLATE NOCASE;
            """);
    }

    public static TabCollection? GetCollection(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return QueryCollections(
            """
            SELECT Id, Name, CreatedAt, UpdatedAt
            FROM TabCollections
            WHERE Id = $id
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("$id", id))
            .FirstOrDefault();
    }

    public static TabCollection? GetCollectionByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var safeName = NormalizeCollectionName(name);
        return QueryCollections(
            """
            SELECT Id, Name, CreatedAt, UpdatedAt
            FROM TabCollections
            WHERE Name = $name COLLATE NOCASE
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("$name", safeName))
            .FirstOrDefault();
    }

    public static IReadOnlyList<TabCollectionItem> GetItems(string collectionNameOrId)
    {
        var collection = GetCollection(collectionNameOrId) ?? GetCollectionByName(collectionNameOrId);
        if (collection is null)
        {
            return [];
        }

        return QueryItems(
            """
            SELECT Id, CollectionId, Url, Title, SortOrder, CreatedAt, UpdatedAt
            FROM TabCollectionItems
            WHERE CollectionId = $collectionId
            ORDER BY SortOrder, UpdatedAt DESC, Title COLLATE NOCASE;
            """,
            command => command.Parameters.AddWithValue("$collectionId", collection.Id));
    }

    private static int GetNextOrder(string collectionId)
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TabCollectionItems WHERE CollectionId = $collectionId";
        cmd.Parameters.AddWithValue("$collectionId", collectionId);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<TabCollection> QueryCollections(
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        using var reader = cmd.ExecuteReader();
        var collections = new List<TabCollection>();

        while (reader.Read())
        {
            collections.Add(new TabCollection(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return collections;
    }

    private static IReadOnlyList<TabCollectionItem> QueryItems(
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        using var reader = cmd.ExecuteReader();
        var items = new List<TabCollectionItem>();

        while (reader.Read())
        {
            items.Add(new TabCollectionItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return items;
    }

    private static string NormalizeCollectionName(string name)
    {
        var safeName = name?.Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException("A collection name is required.", nameof(name));
        }

        return safeName;
    }
}
