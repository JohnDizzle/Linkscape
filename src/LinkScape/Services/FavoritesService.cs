using System.Globalization;

public sealed record FavoriteItem(
    string Id,
    string Url,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

public static class FavoritesService
{
    private const string DbPath = "favorites.db";

    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Favorites(
                Id TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Favorites_Url
            ON Favorites(Url);

            CREATE INDEX IF NOT EXISTS IX_Favorites_Title
            ON Favorites(Title COLLATE NOCASE);
            """;
        cmd.ExecuteNonQuery();
    }

    public static FavoriteItem UpsertFavorite(string? favoriteId, string url, string? title)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("A favorite URL is required.", nameof(url));
        }

        var resolvedId = string.IsNullOrWhiteSpace(favoriteId)
            ? Guid.NewGuid().ToString("N")
            : favoriteId;
        var existing = GetFavorite(resolvedId);
        var safeTitle = string.IsNullOrWhiteSpace(title) ? url : title.Trim();
        var now = DateTime.Now;

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Favorites(Id, Url, Title, CreatedAt, UpdatedAt)
            VALUES($id, $url, $title, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Url = excluded.Url,
                Title = excluded.Title,
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$id", resolvedId);
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", safeTitle);
        cmd.Parameters.AddWithValue("$createdAt", (existing?.CreatedAt ?? now).ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        cmd.ExecuteNonQuery();

        return GetFavorite(resolvedId)
            ?? new FavoriteItem(
                resolvedId,
                url,
                safeTitle,
                existing?.CreatedAt ?? now,
                now);
    }

    public static bool RemoveFavorite(string favoriteId)
    {
        if (string.IsNullOrWhiteSpace(favoriteId))
        {
            return false;
        }

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Favorites WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", favoriteId);

        return cmd.ExecuteNonQuery() > 0;
    }

    public static void ClearFavorites()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Favorites";

        cmd.ExecuteNonQuery();
    }

    public static FavoriteItem? GetFavorite(string favoriteId)
    {
        if (string.IsNullOrWhiteSpace(favoriteId))
        {
            return null;
        }

        return QueryFavorites(
            """
            SELECT Id, Url, Title, CreatedAt, UpdatedAt
            FROM Favorites
            WHERE Id = $id
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("$id", favoriteId))
            .FirstOrDefault();
    }

    public static IReadOnlyList<FavoriteItem> GetFavorites()
    {
        return QueryFavorites(
            """
            SELECT Id, Url, Title, CreatedAt, UpdatedAt
            FROM Favorites
            ORDER BY UpdatedAt DESC, Title COLLATE NOCASE, Url COLLATE NOCASE;
            """);
    }

    public static IReadOnlyList<FavoriteItem> SearchFavorites(string? query)
    {
        var search = $"%{(query ?? string.Empty).Trim()}%";

        return QueryFavorites(
            """
            SELECT Id, Url, Title, CreatedAt, UpdatedAt
            FROM Favorites
            WHERE $query = '%%'
                OR Title LIKE $query
                OR Url LIKE $query
            ORDER BY UpdatedAt DESC, Title COLLATE NOCASE, Url COLLATE NOCASE;
            """,
            command => command.Parameters.AddWithValue("$query", search));
    }

    private static IReadOnlyList<FavoriteItem> QueryFavorites(
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        using var reader = cmd.ExecuteReader();
        var items = new List<FavoriteItem>();

        while (reader.Read())
        {
            items.Add(new FavoriteItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return items;
    }
}
