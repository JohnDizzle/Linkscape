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
    private static readonly string DbConnectionString = LinkScapeCachePaths.GetDatabaseConnectionString("history.db");
    private static readonly HashSet<string> VolatileQueryParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_",
        "code",
        "client-request-id",
        "fbclid",
        "msclkid",
        "nonce",
        "redirectid",
        "session_state",
        "state"
    };

    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS HistoryItems(
                Url TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                FirstVisitedAt TEXT NOT NULL,
                LastVisitedAt TEXT NOT NULL,
                VisitCount INTEGER NOT NULL DEFAULT 1
            );

            DELETE FROM HistoryItems
            WHERE Url IS NULL;

            CREATE INDEX IF NOT EXISTS IX_HistoryItems_LastVisitedAt
            ON HistoryItems(LastVisitedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_HistoryItems_VisitCount
            ON HistoryItems(VisitCount DESC, LastVisitedAt DESC);
            """;

        cmd.ExecuteNonQuery();
    }

    public static void RecordVisit(string url, string? title)
    {
        var normalizedUrl = NormalizeHistoryUrl(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return;
        }

        var now = DateTime.Now;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? normalizedUrl : title;

        using var conn = new SqliteConnection(DbConnectionString);
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

        cmd.Parameters.AddWithValue("$url", normalizedUrl);
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
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        foreach (var item in items)
        {
            var normalizedUrl = NormalizeHistoryUrl(item.Url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                continue;
            }

            var safeTitle = string.IsNullOrWhiteSpace(item.Title) ? normalizedUrl : item.Title;

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

            cmd.Parameters.AddWithValue("$url", normalizedUrl);
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
        var normalizedUrl = NormalizeHistoryUrl(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return;
        }

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HistoryItems WHERE Url = $url";
        cmd.Parameters.AddWithValue("$url", normalizedUrl);

        cmd.ExecuteNonQuery();
    }

    public static void ClearHistory()
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HistoryItems";

        cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<HistoryItem> QueryHistory(
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configure?.Invoke(cmd);

        using var reader = cmd.ExecuteReader();
        var items = new List<HistoryItem>();

        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3) || reader.IsDBNull(4))
            {
                continue;
            }

            items.Add(new HistoryItem(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt32(4)));
        }

        return items;
    }

    private static string? NormalizeHistoryUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();

        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.IsFile)
        {
            return null;
        }

        if (TryNormalizeYouTubeUrl(uri, out var normalizedYouTubeUrl))
        {
            return normalizedYouTubeUrl;
        }

        var filteredQueryParameters = ParseQueryString(uri.Query)
            .Where(static kvp => !IsVolatileHistoryParameter(kvp.Key))
            .OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static kvp => kvp.Value, StringComparer.Ordinal)
            .ToArray();

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = BuildQueryString(filteredQueryParameters)
        };

        var normalizedUrl = builder.Uri.AbsoluteUri;
        return builder.Path == "/"
            ? normalizedUrl.TrimEnd('/')
            : normalizedUrl;
    }

    private static bool TryNormalizeYouTubeUrl(Uri uri, out string? normalizedUrl)
    {
        normalizedUrl = null;

        if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                normalizedUrl = $"https://www.youtube.com/watch?v={videoId}";
                return true;
            }

            return false;
        }

        if (!uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, "/watch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQueryString(uri.Query);
        if (!query.TryGetValue("v", out var canonicalVideoId) || string.IsNullOrWhiteSpace(canonicalVideoId))
        {
            return false;
        }

        normalizedUrl = $"https://www.youtube.com/watch?v={canonicalVideoId}";
        return true;
    }

    private static bool IsVolatileHistoryParameter(string key)
    {
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || VolatileQueryParameterNames.Contains(key);
    }

    private static IReadOnlyDictionary<string, string> ParseQueryString(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1])
                : string.Empty;

            values[key] = value;
        }

        return values;
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> queryParameters)
    {
        return string.Join(
            "&",
            queryParameters.Select(static kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
    }
}