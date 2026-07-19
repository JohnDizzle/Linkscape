public static class BrowserDataToolService
{
    public const string HistoryTodayToolName = "browser.history.today";
    public const string HistoryRecentToolName = "browser.history.recent";
    public const string HistoryMostVisitedToolName = "browser.history.mostVisited";
    public const string FavoritesSummaryToolName = "browser.favorites.summary";
    public const string FavoritesSearchToolName = "browser.favorites.search";

    public static IReadOnlyList<ChatToolStatus> GetTools() =>
    [
        new(HistoryTodayToolName, true, "Summarizes today's browser history activity."),
        new(HistoryRecentToolName, true, "Summarizes recent browser history activity."),
        new(HistoryMostVisitedToolName, true, "Summarizes most visited browser history entries."),
        new(FavoritesSummaryToolName, true, "Summarizes saved favorites."),
        new(FavoritesSearchToolName, true, "Searches saved favorites by title or URL.")
    ];

    public static BrowserDataAssistantResult Invoke(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments = null)
    {
        arguments ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return toolName switch
        {
            HistoryTodayToolName => BrowserDataAssistantService.BuildTodayHistoryReport(),
            HistoryRecentToolName => BrowserDataAssistantService.BuildRecentHistoryReport(),
            HistoryMostVisitedToolName => BrowserDataAssistantService.BuildMostVisitedHistoryReport(),
            FavoritesSummaryToolName => BrowserDataAssistantService.BuildFavoritesSummaryReport(),
            FavoritesSearchToolName => BrowserDataAssistantService.BuildFavoritesSearchReport(arguments.TryGetValue("query", out var query) ? query : string.Empty),
            _ => new BrowserDataAssistantResult($"## Unknown browser data tool\nTool `{toolName}` is not registered.")
        };
    }

    public static string SelectToolName(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;

        if ((prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase)) &&
            (prompt.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("find", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("matching", StringComparison.OrdinalIgnoreCase)))
        {
            return FavoritesSearchToolName;
        }

        if (prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase))
        {
            return FavoritesSummaryToolName;
        }

        if (prompt.Contains("most", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("top", StringComparison.OrdinalIgnoreCase))
        {
            return HistoryMostVisitedToolName;
        }

        if (prompt.Contains("today", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("active", StringComparison.OrdinalIgnoreCase))
        {
            return HistoryTodayToolName;
        }

        return HistoryRecentToolName;
    }
}
