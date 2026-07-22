using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

public sealed record HistoryGroupSqlRequest(
    string GroupBy,
    string Include,
    string State,
    string SortBy,
    int Limit,
    DateTime? StartedAt = null,
    DateTime? EndedAt = null,
    string? Url = null,
    IReadOnlyList<string>? SearchTerms = null);

public sealed record HistoryGroupSqlRow(
    string Period,
    int PageCount,
    int VisitCount,
    int FavoriteCount,
    int CollectionLinkCount,
    DateTime? FirstVisitedAt,
    DateTime? LastVisitedAt);

public sealed record HistoryGroupSqlReport(
    HistoryGroupSqlRequest Request,
    IReadOnlyList<HistoryGroupSqlRow> Rows);

public static class HistoryGroupSqlService
{
    private const int DefaultLimit = 24;
    private const int MaximumLimit = 120;

    public static HistoryGroupSqlReport Query(HistoryGroupSqlRequest request)
    {
        EnsureDatabases();

        var normalized = NormalizeRequest(request);
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        AttachDatabase(conn, "history", "history.db");
        AttachDatabase(conn, "favorites", "favorites.db");
        AttachDatabase(conn, "collections", "tabCollections.db");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSql(normalized);
        cmd.Parameters.AddWithValue("$state", normalized.State);
        cmd.Parameters.AddWithValue("$startedAt", normalized.StartedAt?.ToString("O") ?? string.Empty);
        cmd.Parameters.AddWithValue("$endedAt", normalized.EndedAt?.ToString("O") ?? string.Empty);
        cmd.Parameters.AddWithValue("$url", normalized.Url ?? string.Empty);
        cmd.Parameters.AddWithValue("$limit", normalized.Limit);
        for (var index = 0; index < (normalized.SearchTerms?.Count ?? 0); index++)
        {
            cmd.Parameters.AddWithValue($"$term{index}", $"%{normalized.SearchTerms![index]}%");
        }

        LocalMcpDiagnostics.Trace(
            "HistoryGroupSql",
            $"groupBy={normalized.GroupBy}, include={normalized.Include}, state={normalized.State}, sortBy={normalized.SortBy}, url={normalized.Url}, search={string.Join("|", normalized.SearchTerms ?? [])}, limit={normalized.Limit}");

        using var reader = cmd.ExecuteReader();
        var rows = new List<HistoryGroupSqlRow>();

        while (reader.Read())
        {
            rows.Add(new HistoryGroupSqlRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                ParseOptionalDate(reader, 5),
                ParseOptionalDate(reader, 6)));
        }

        return new HistoryGroupSqlReport(normalized, rows);
    }

    public static HistoryGroupSqlRequest FromArguments(IReadOnlyDictionary<string, string> arguments)
    {
        var prompt = GetArgument(arguments, "prompt", "query");
        var groupBy = GetArgument(arguments, "groupBy", "period");
        var include = GetArgument(arguments, "include", "includes");
        var state = GetArgument(arguments, "state", "historyState");
        var sortBy = GetArgument(arguments, "sortBy", "sort");
        var limitText = GetArgument(arguments, "limit");
        var startedAtText = GetArgument(arguments, "startedAt", "from");
        var endedAtText = GetArgument(arguments, "endedAt", "to");
        var url = GetArgument(arguments, "url", "urlFilter");
        var searchText = GetArgument(arguments, "search", "searchTerms", "term", "terms");

        var parsed = ParsePrompt(prompt);
        var startedAt = DateTime.TryParse(startedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedStartedAt)
            ? parsedStartedAt
            : parsed.StartedAt;
        var endedAt = DateTime.TryParse(endedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedEndedAt)
            ? parsedEndedAt
            : parsed.EndedAt;

        return NormalizeRequest(new HistoryGroupSqlRequest(
            string.IsNullOrWhiteSpace(groupBy) ? parsed.GroupBy : groupBy,
            string.IsNullOrWhiteSpace(include) ? parsed.Include : include,
            string.IsNullOrWhiteSpace(state) ? parsed.State : state,
            string.IsNullOrWhiteSpace(sortBy) ? parsed.SortBy : sortBy,
            int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) ? limit : parsed.Limit,
            startedAt,
            endedAt,
            string.IsNullOrWhiteSpace(url) ? parsed.Url : url,
            string.IsNullOrWhiteSpace(searchText) ? parsed.SearchTerms : SplitSearchTerms(searchText)));
    }

    private static HistoryGroupSqlRequest ParsePrompt(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var groupBy = Regex.IsMatch(prompt, @"\b(day|daily|today)\b", RegexOptions.IgnoreCase)
            ? "day"
            : Regex.IsMatch(prompt, @"\b(year|yearly|annual)\b", RegexOptions.IgnoreCase)
                ? "year"
                : "month";

        var includeParts = new List<string> { "visits" };

        if (Regex.IsMatch(prompt, @"\bfavou?rite|bookmark\b", RegexOptions.IgnoreCase))
        {
            includeParts.Add("favorites");
        }

        if (Regex.IsMatch(prompt, @"\bcollection|collections\b", RegexOptions.IgnoreCase))
        {
            includeParts.Add("collections");
        }

        var include = string.Join(',', includeParts.Distinct(StringComparer.OrdinalIgnoreCase));
        var state = Regex.IsMatch(prompt, @"\barchive|archived|cold\b", RegexOptions.IgnoreCase)
            ? Regex.IsMatch(prompt, @"\bcurrent|active|both|all\b", RegexOptions.IgnoreCase) ? "both" : "archived"
            : "current";
        var sortBy = Regex.IsMatch(prompt, @"\bfavou?rite|bookmark\b", RegexOptions.IgnoreCase)
            ? "favorites"
            : Regex.IsMatch(prompt, @"\bcollection|collections\b", RegexOptions.IgnoreCase)
                ? "collections"
                : Regex.IsMatch(prompt, @"\bmost|top|visit|visited\b", RegexOptions.IgnoreCase)
                    ? "visits"
                    : "lastVisit";

        return new HistoryGroupSqlRequest(groupBy, include, state, sortBy, DefaultLimit, Url: ExtractUrl(prompt), SearchTerms: ExtractSearchTerms(prompt));
    }

    private static HistoryGroupSqlRequest NormalizeRequest(HistoryGroupSqlRequest request)
    {
        var groupBy = NormalizeChoice(request.GroupBy, "month", "day", "month", "year");
        var state = NormalizeChoice(request.State, "current", "current", "archived", "both");
        var sortBy = NormalizeChoice(request.SortBy, "lastVisit", "lastVisit", "visits", "favorites", "collections", "period");
        var include = NormalizeInclude(request.Include);
        var limit = Math.Clamp(request.Limit <= 0 ? DefaultLimit : request.Limit, 1, MaximumLimit);
        var url = StoredUrlNormalizer.Normalize(request.Url);
        var searchTerms = (request.SearchTerms ?? [])
            .Select(term => term.Trim())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return request with
        {
            GroupBy = groupBy,
            Include = include,
            State = state,
            SortBy = sortBy,
            Limit = limit,
            Url = url,
            SearchTerms = searchTerms
        };
    }

    private static string BuildSql(HistoryGroupSqlRequest request)
    {
        var periodExpression = request.GroupBy switch
        {
            "day" => "substr(LastVisitedAt, 1, 10)",
            "year" => "substr(LastVisitedAt, 1, 4)",
            _ => "substr(LastVisitedAt, 1, 7)"
        };
        var orderBy = request.SortBy switch
        {
            "visits" => "VisitCount DESC, LastVisitedAt DESC",
            "favorites" => "FavoriteCount DESC, LastVisitedAt DESC",
            "collections" => "CollectionLinkCount DESC, LastVisitedAt DESC",
            "period" => "Period DESC",
            _ => "LastVisitedAt DESC"
        };
        var searchFilter = request.SearchTerms?.Count > 0
            ? "AND (" + string.Join(" OR ", request.SearchTerms.Select((_, index) => $"(Url LIKE $term{index} OR Title LIKE $term{index})")) + ")"
            : string.Empty;

        return $$"""
            WITH HistorySource AS (
                SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount, 'current' AS HistoryState
                FROM history.HistoryItems
                WHERE ($state = 'current' OR $state = 'both')
                    AND ($startedAt = '' OR LastVisitedAt >= $startedAt)
                    AND ($endedAt = '' OR LastVisitedAt < $endedAt)
                    AND ($url = '' OR Url = $url)
                    {{searchFilter}}
                UNION ALL
                SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount, 'archived' AS HistoryState
                FROM history.HistoryArchiveItems
                WHERE ($state = 'archived' OR $state = 'both')
                    AND ($startedAt = '' OR LastVisitedAt >= $startedAt)
                    AND ($endedAt = '' OR LastVisitedAt < $endedAt)
                    AND ($url = '' OR Url = $url)
                    {{searchFilter}}
            ),
            RelatedByUrl AS (
                SELECT
                    h.Url,
                    h.FirstVisitedAt,
                    h.LastVisitedAt,
                    h.VisitCount,
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM favorites.Favorites f
                        WHERE f.Url = h.Url
                    ) THEN 1 ELSE 0 END AS IsFavorite,
                    (
                        SELECT COUNT(DISTINCT ci.CollectionId)
                        FROM collections.TabCollectionItems ci
                        WHERE ci.Url = h.Url
                    ) AS CollectionLinks
                FROM HistorySource h
            )
            SELECT
                {{periodExpression}} AS Period,
                COUNT(DISTINCT Url) AS PageCount,
                COALESCE(SUM(VisitCount), 0) AS VisitCount,
                COALESCE(SUM(IsFavorite), 0) AS FavoriteCount,
                COALESCE(SUM(CollectionLinks), 0) AS CollectionLinkCount,
                MIN(FirstVisitedAt) AS FirstVisitedAt,
                MAX(LastVisitedAt) AS LastVisitedAt
            FROM RelatedByUrl
            GROUP BY Period
            ORDER BY {{orderBy}}
            LIMIT $limit;
            """;
    }

    private static string NormalizeInclude(string include)
    {
        var value = include?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return "visits";
        }

        var parts = Regex.Split(value, @"[,\s]+")
            .Select(part => part.Trim().ToLowerInvariant())
            .Where(part => part is "visits" or "favorites" or "collections")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? "visits" : string.Join(',', parts);
    }

    private static string NormalizeChoice(string value, string fallback, params string[] allowed)
    {
        value = value?.Trim() ?? string.Empty;
        foreach (var choice in allowed)
        {
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        return fallback;
    }

    private static string ExtractUrl(string prompt)
    {
        var match = Regex.Match(prompt ?? string.Empty, @"https?://[^\s\)\]\}>,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static string[] ExtractSearchTerms(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var quoted = Regex.Match(prompt, "\"(?<terms>[^\"]+)\"");
        if (quoted.Success)
        {
            return SplitSearchTerms(quoted.Groups["terms"].Value);
        }

        var forMatch = Regex.Match(
            prompt,
            @"\b(?:about|for)\s+(?<terms>.+?)(?:\s+\b(?:by|with|group|grouped|during|in)\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (forMatch.Success)
        {
            return SplitSearchTerms(CleanSearchTerms(forMatch.Groups["terms"].Value));
        }

        var leadingMatch = Regex.Match(
            prompt,
            @"^(?<terms>.+?)\s+\b(?:group|grouped|report)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return leadingMatch.Success
            ? SplitSearchTerms(CleanSearchTerms(leadingMatch.Groups["terms"].Value))
            : [];
    }

    private static string CleanSearchTerms(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\b(report|history|of|my|the|visited|pages?|page|visit|visits|favorite|favorites|bookmark|bookmarks|collection|collections|current|archived|day|month|year|by|with|for|about)\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '!', '?', '"', '\'');
    }

    private static string[] SplitSearchTerms(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\band\b", ",", RegexOptions.IgnoreCase);
        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanSearchTerms)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AttachDatabase(SqliteConnection conn, string schema, string databaseFileName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ATTACH DATABASE ${schema}Path AS {schema};";
        cmd.Parameters.AddWithValue($"${schema}Path", Path.Combine(LinkScapeCachePaths.CacheDirectory, databaseFileName));
        cmd.ExecuteNonQuery();
    }

    private static void EnsureDatabases()
    {
        HistoryPersistenceService.EnsureDatabase();
        FavoritesService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();
    }

    private static DateTime? ParseOptionalDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string GetArgument(IReadOnlyDictionary<string, string> arguments, params string[] names)
    {
        foreach (var name in names)
        {
            if (arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
