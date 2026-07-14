using Microsoft.Data.Sqlite;
using System.Globalization;

public sealed record HistoryItem(
    string Url,
    string Title,
    DateTime FirstVisitedAt,
    DateTime LastVisitedAt,
    int VisitCount) : IReactorKeyed
{
    string IReactorKeyed.Key => Url;
}

public static class HistoryPersistenceService
{
    private const string DbPath = "history.db";

    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS HistoryItems(
                Url TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                FirstVisitedAt TEXT NOT NULL,
                LastVisitedAt TEXT NOT NULL,
                VisitCount INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS IX_HistoryItems_LastVisitedAt
            ON HistoryItems(LastVisitedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_HistoryItems_VisitCount
            ON HistoryItems(VisitCount DESC, LastVisitedAt DESC);
            """;

        cmd.ExecuteNonQuery();
    }

    public static void RecordVisit(string url, string? title)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var now = DateTime.Now;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? url : title;

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO HistoryItems(
                Url,
                Title,
                FirstVisitedAt,
                LastVisitedAt,
                VisitCount
            )
            VALUES(
                $url,
                $title,
                $now,
                $now,
                1
            )
            ON CONFLICT(Url) DO UPDATE SET
                Title = excluded.Title,
                LastVisitedAt = excluded.LastVisitedAt,
                VisitCount = HistoryItems.VisitCount + 1;
            """;

        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", safeTitle);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));

        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<HistoryItem> GetRecentHistory(int limit = 100)
    {
        return QueryHistory(
            """
            SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount
            FROM HistoryItems
            ORDER BY LastVisitedAt DESC
            LIMIT $limit;
            """,
            command => command.Parameters.AddWithValue("$limit", limit));
    }

    public static IReadOnlyList<HistoryItem> GetMostVisited(int limit = 12)
    {
        return QueryHistory(
            """
            SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount
            FROM HistoryItems
            ORDER BY VisitCount DESC, LastVisitedAt DESC
            LIMIT $limit;
            """,
            command => command.Parameters.AddWithValue("$limit", limit));
    }

    public static IReadOnlyList<HistoryItem> SearchHistory(string? query, int limit = 100)
    {
        var search = $"%{(query ?? string.Empty).Trim()}%";

        return QueryHistory(
            """
            SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount
            FROM HistoryItems
            WHERE $query = '%%'
                OR Url LIKE $query
                OR Title LIKE $query
            ORDER BY LastVisitedAt DESC
            LIMIT $limit;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$query", search);
                command.Parameters.AddWithValue("$limit", limit);
            });
    }

    public static void UpsertImportedHistory(IEnumerable<HistoryItem> items)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var transaction = conn.BeginTransaction();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            var safeTitle = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO HistoryItems(
                    Url,
                    Title,
                    FirstVisitedAt,
                    LastVisitedAt,
                    VisitCount
                )
                VALUES(
                    $url,
                    $title,
                    $firstVisitedAt,
                    $lastVisitedAt,
                    $visitCount
                )
                ON CONFLICT(Url) DO UPDATE SET
                    Title = CASE
                        WHEN excluded.Title IS NULL OR excluded.Title = '' THEN HistoryItems.Title
                        ELSE excluded.Title
                    END,
                    FirstVisitedAt = CASE
                        WHEN HistoryItems.FirstVisitedAt <= excluded.FirstVisitedAt THEN HistoryItems.FirstVisitedAt
                        ELSE excluded.FirstVisitedAt
                    END,
                    LastVisitedAt = CASE
                        WHEN HistoryItems.LastVisitedAt >= excluded.LastVisitedAt THEN HistoryItems.LastVisitedAt
                        ELSE excluded.LastVisitedAt
                    END,
                    VisitCount = CASE
                        WHEN HistoryItems.VisitCount >= excluded.VisitCount THEN HistoryItems.VisitCount
                        ELSE excluded.VisitCount
                    END;
                """;

            cmd.Parameters.AddWithValue("$url", item.Url);
            cmd.Parameters.AddWithValue("$title", safeTitle);
            cmd.Parameters.AddWithValue("$firstVisitedAt", item.FirstVisitedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$lastVisitedAt", item.LastVisitedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$visitCount", Math.Max(1, item.VisitCount));

            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public static void DeleteUrl(string url)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HistoryItems WHERE Url = $url";
        cmd.Parameters.AddWithValue("$url", url);

        cmd.ExecuteNonQuery();
    }

    public static void ClearHistory()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HistoryItems";

        cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<HistoryItem> QueryHistory(
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        using var reader = cmd.ExecuteReader();
        var items = new List<HistoryItem>();

        while (reader.Read())
        {
            items.Add(new HistoryItem(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt32(4)));
        }

        return items;
    }
}