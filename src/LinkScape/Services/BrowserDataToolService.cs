public static class BrowserDataToolService
{
    public const string HistoryTodayToolName = "browser.history.today";
    public const string HistoryRecentToolName = "browser.history.recent";
    public const string HistoryMostVisitedToolName = "browser.history.mostVisited";
    public const string HistorySearchToolName = "browser.history.search";
    public const string HistoryPeriodToolName = "browser.history.period";
    public const string HistoryArchiveToolName = "browser.history.archive";
    public const string FavoritesSummaryToolName = "browser.favorites.summary";
    public const string FavoritesSearchToolName = "browser.favorites.search";
    public const string TabsSummaryToolName = "browser.tabs.summary";
    public const string CollectionsListToolName = "browser.collections.list";
    public const string CollectionsSummaryToolName = "browser.collections.summary";
    public const string CollectionsAddItemToolName = "browser.collections.addItem";
    public const string CollectionsRemoveItemToolName = "browser.collections.removeItem";
    public const string CollectionsRenameToolName = "browser.collections.rename";
    public const string CollectionsSetStartupToolName = "browser.collections.setStartup";

    public static IReadOnlyList<ChatToolStatus> GetTools() =>
    [
        new(HistoryTodayToolName, true, "Summarizes today's browser history activity."),
        new(HistoryRecentToolName, true, "Summarizes recent browser history activity."),
        new(HistoryMostVisitedToolName, true, "Summarizes most visited browser history entries."),
        new(HistorySearchToolName, true, "Searches browser history by title or URL, including starts-with matching."),
        new(HistoryPeriodToolName, true, "Shows browser history for a month, year, or explicit date period."),
        new(HistoryArchiveToolName, true, "Archives browser history for a month or year into the archive table."),
        new(FavoritesSummaryToolName, true, "Summarizes saved favorites."),
        new(FavoritesSearchToolName, true, "Searches saved favorites by title or URL."),
        new(TabsSummaryToolName, true, "Summarizes saved/restored browser tabs and the selected tab."),
        new(CollectionsListToolName, true, "Lists saved tab collections."),
        new(CollectionsSummaryToolName, true, "Summarizes a saved tab collection."),
        new(CollectionsAddItemToolName, true, "Adds a URL or the active page to a tab collection."),
        new(CollectionsRemoveItemToolName, true, "Removes a URL or the active page from a tab collection."),
        new(CollectionsRenameToolName, true, "Renames a tab collection."),
        new(CollectionsSetStartupToolName, true, "Sets the collection LinkScape should open on startup.")
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
            HistorySearchToolName => BrowserDataAssistantService.BuildHistorySearchReport(GetArgument(arguments, "query", "prompt")),
            HistoryPeriodToolName => BrowserDataAssistantService.BuildHistoryPeriodReport(GetArgument(arguments, "period", "query", "prompt")),
            HistoryArchiveToolName => BrowserDataAssistantService.BuildHistoryArchiveReport(GetArgument(arguments, "period", "query", "prompt")),
            FavoritesSummaryToolName => BrowserDataAssistantService.BuildFavoritesSummaryReport(),
            FavoritesSearchToolName => BrowserDataAssistantService.BuildFavoritesSearchReport(arguments.TryGetValue("query", out var query) ? query : string.Empty),
            TabsSummaryToolName => BrowserDataAssistantService.BuildTabsSummaryReport(),
            CollectionsListToolName => BrowserDataAssistantService.BuildCollectionsListReport(),
            CollectionsSummaryToolName => BrowserDataAssistantService.BuildCollectionSummaryReport(GetArgument(arguments, "collection", "query", "prompt")),
            CollectionsAddItemToolName => BrowserDataAssistantService.BuildAddCollectionItemReport(
                GetArgument(arguments, "collection", "query", "prompt"),
                GetArgument(arguments, "url", "activeUrl"),
                GetArgument(arguments, "title", "activeTitle")),
            CollectionsRemoveItemToolName => BrowserDataAssistantService.BuildRemoveCollectionItemReport(
                GetArgument(arguments, "collection", "query", "prompt"),
                GetArgument(arguments, "url", "activeUrl")),
            CollectionsRenameToolName => BrowserDataAssistantService.BuildRenameCollectionReport(
                GetArgument(arguments, "collection", "currentName", "prompt"),
                GetArgument(arguments, "nextName", "newName")),
            CollectionsSetStartupToolName => BrowserDataAssistantService.BuildSetStartupCollectionReport(GetArgument(arguments, "collection", "query", "prompt")),
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

        if (IsCollectionPrompt(prompt))
        {
            if (prompt.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("take out", StringComparison.OrdinalIgnoreCase))
            {
                toolName = CollectionsRemoveItemToolName;
                return true;
            }

            if (prompt.Contains("add", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("save", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("put", StringComparison.OrdinalIgnoreCase))
            {
                toolName = CollectionsAddItemToolName;
                return true;
            }

            if (prompt.Contains("rename", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("edit", StringComparison.OrdinalIgnoreCase))
            {
                toolName = CollectionsRenameToolName;
                return true;
            }

            if (prompt.Contains("startup", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("start up", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("open on launch", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("launch", StringComparison.OrdinalIgnoreCase))
            {
                toolName = CollectionsSetStartupToolName;
                return true;
            }

            if (prompt.Contains("list", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("show all", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("show my collection", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("show collections", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("my collections", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("what are", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("what collections", StringComparison.OrdinalIgnoreCase))
            {
                toolName = CollectionsListToolName;
                return true;
            }

            toolName = CollectionsSummaryToolName;
            return true;
        }

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

        if (prompt.Contains("archive", StringComparison.OrdinalIgnoreCase))
        {
            toolName = HistoryArchiveToolName;
            return true;
        }

        if (IsHistoryPeriodPrompt(prompt))
        {
            toolName = HistoryPeriodToolName;
            return true;
        }

        if (IsHistorySearchPrompt(prompt))
        {
            toolName = HistorySearchToolName;
            return true;
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
        prompt.Contains("anything with", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("starts with", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("page", StringComparison.OrdinalIgnoreCase);

    private static bool IsHistorySearchPrompt(string prompt) =>
        prompt.Contains("anything with", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("starts with", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("start with", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("with \"", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("named", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("name", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("search", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("find", StringComparison.OrdinalIgnoreCase);

    private static bool IsHistoryPeriodPrompt(string prompt) =>
        prompt.Contains("this month", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("this year", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("last month", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("last year", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("january", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("february", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("march", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("april", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("may", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("june", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("july", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("august", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("september", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("october", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("november", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("december", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(prompt, @"\b20\d{2}\b");

    private static bool IsTabPrompt(string prompt) =>
        prompt.Contains("tab", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("tabs", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("session", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("restored", StringComparison.OrdinalIgnoreCase);

    private static bool IsCollectionPrompt(string prompt) =>
        prompt.Contains("collection", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("collections", StringComparison.OrdinalIgnoreCase);

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
