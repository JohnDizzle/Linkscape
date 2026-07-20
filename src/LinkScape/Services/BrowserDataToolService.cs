public static class BrowserDataToolService
{
    public const string HistoryTodayToolName = "browser.history.today";
    public const string HistoryRecentToolName = "browser.history.recent";
    public const string HistoryMostVisitedToolName = "browser.history.mostVisited";
    public const string FavoritesSummaryToolName = "browser.favorites.summary";
    public const string FavoritesSearchToolName = "browser.favorites.search";
    public const string TabsSummaryToolName = "browser.tabs.summary";

    public static IReadOnlyList<ChatToolStatus> GetTools() =>
    [
        new(HistoryTodayToolName, true, "Summarizes today's browser history activity."),
        new(HistoryRecentToolName, true, "Summarizes recent browser history activity."),
        new(HistoryMostVisitedToolName, true, "Summarizes most visited browser history entries."),
        new(FavoritesSummaryToolName, true, "Summarizes saved favorites."),
        new(FavoritesSearchToolName, true, "Searches saved favorites by title or URL."),
        new(TabsSummaryToolName, true, "Summarizes saved/restored browser tabs and the selected tab.")
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
            TabsSummaryToolName => BrowserDataAssistantService.BuildTabsSummaryReport(),
            _ => new BrowserDataAssistantResult($"## Unknown browser data tool\nTool `{toolName}` is not registered.")
        };
    }

    public static string SelectToolName(string prompt)
    {
        if (!TrySelectToolName(prompt, out var toolName))
        {
            return HistoryRecentToolName;
        }

        return toolName;
    }

    public static bool TrySelectToolName(string prompt, out string toolName)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        toolName = HistoryRecentToolName;

        if ((prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase)) &&
            (prompt.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("find", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("matching", StringComparison.OrdinalIgnoreCase)))
        {
            toolName = FavoritesSearchToolName;
            return true;
        }

        if (prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase))
        {
            toolName = FavoritesSummaryToolName;
            return true;
        }

        if (IsTabPrompt(prompt))
        {
            toolName = TabsSummaryToolName;
            return true;
        }

        if (!IsBrowserHistoryPrompt(prompt))
        {
            return false;
        }

        if (prompt.Contains("most", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("top", StringComparison.OrdinalIgnoreCase))
        {
            toolName = HistoryMostVisitedToolName;
            return true;
        }

        if (prompt.Contains("today", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("active", StringComparison.OrdinalIgnoreCase))
        {
            toolName = HistoryTodayToolName;
            return true;
        }

        toolName = HistoryRecentToolName;
        return true;
    }

    private static bool IsBrowserHistoryPrompt(string prompt) =>
        prompt.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("history", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("visited", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("visit", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("activity", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("active site", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("recent site", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("page", StringComparison.OrdinalIgnoreCase);

    private static bool IsTabPrompt(string prompt) =>
        prompt.Contains("tab", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("tabs", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("session", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("restored", StringComparison.OrdinalIgnoreCase);
}
