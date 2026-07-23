using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using LinkScape.Models;

public sealed record BrowserDataAssistantResult(
    string Markdown,
    string? Html = null);

public static class BrowserDataAssistantService
{
    public static BrowserDataAssistantResult Answer(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;

        if (IsFavoritesPrompt(prompt))
        {
            return BuildFavoritesReport(prompt);
        }

        return BuildHistoryReport(prompt);
    }

    public static BrowserDataAssistantResult BuildTodayHistoryReport()
    {
        var todayItems = HistoryPersistenceService.GetTodayHistory(100);
        if (todayItems.Count > 0)
        {
            return BuildHistoryReport("Today's browser activity", todayItems);
        }

        var recentItems = HistoryPersistenceService.GetRecentHistory(25);
        return BuildHistoryReport("No browser activity recorded today", recentItems, "Showing recent stored history instead.");
    }

    public static BrowserDataAssistantResult BuildRecentHistoryReport()
    {
        return BuildHistoryReport("Recent browser activity", HistoryPersistenceService.GetRecentHistory(50));
    }

    public static BrowserDataAssistantResult BuildMostVisitedHistoryReport()
    {
        return BuildHistoryReport("Most visited browser pages", HistoryPersistenceService.GetMostVisited(25));
    }

    public static BrowserDataAssistantResult BuildHistorySearchReport(string prompt)
    {
        var query = ExtractHistorySearchQuery(prompt);
        if (string.IsNullOrWhiteSpace(query))
        {
            return new BrowserDataAssistantResult("## History search\nTell me what to search for, like `show me history with \"microsoft\"` or `history starts with microsoft`.");
        }

        var startsWith = prompt.Contains("starts with", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("start with", StringComparison.OrdinalIgnoreCase);
        var items = HistoryPersistenceService.SearchHistory(query, startsWith, 100);
        var mode = startsWith ? "starts with" : "contains";

        return BuildHistoryReport(
            $"History where title or URL {mode} '{query}'",
            items,
            items.Count == 0 ? "No matching active history items were found." : null);
    }

    public static BrowserDataAssistantResult BuildHistoryPeriodReport(string prompt)
    {
        if (!TryExtractHistoryPeriod(prompt, out var startedAt, out var endedAt, out var label))
        {
            return new BrowserDataAssistantResult("## History period\nTell me a month or year, like `history for this month`, `history for June`, or `history for 2025`.");
        }

        var searchTerms = ExtractHistoryReportSearchTerms(prompt);
        var items = searchTerms.Length == 0
            ? HistoryPersistenceService.GetHistoryBetween(startedAt, endedAt, 200)
            : HistoryPersistenceService.GetHistoryBetween(startedAt, endedAt, searchTerms, 200);
        var note = searchTerms.Length == 0
            ? items.Count == 0 ? $"No active history items were found for {label}." : null
            : $"Search group: **{EscapeMarkdown(string.Join(", ", searchTerms))}**";

        return BuildHistoryReport(
            $"History for {label}",
            items,
            note);
    }

    public static BrowserDataAssistantResult BuildHistoryGroupReport(IReadOnlyDictionary<string, string> arguments)
    {
        var report = HistoryGroupSqlService.Query(HistoryGroupSqlService.FromArguments(arguments));
        var request = report.Request;
        var markdown = new StringBuilder();
        markdown.AppendLine($"## History grouped by {request.GroupBy}");
        markdown.AppendLine();
        markdown.AppendLine($"- State: **{EscapeMarkdown(request.State)}**");
        markdown.AppendLine($"- Includes: **{EscapeMarkdown(request.Include)}**");
        markdown.AppendLine($"- Sort: **{EscapeMarkdown(request.SortBy)}**");
        if (request.SearchTerms?.Count > 0)
        {
            markdown.AppendLine($"- Search group: **{EscapeMarkdown(string.Join(", ", request.SearchTerms))}**");
        }
        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            markdown.AppendLine($"- URL: **{MarkdownLink(request.Url, request.Url)}**");
        }
        markdown.AppendLine($"- Groups returned: **{report.Rows.Count}**");
        markdown.AppendLine();

        if (report.Rows.Count == 0)
        {
            markdown.AppendLine("No matching history groups were found.");
            return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), BuildHistoryGroupHtmlReport(report));
        }

        markdown.AppendLine("### Periods");
        markdown.AppendLine();

        foreach (var row in report.Rows.Take(12))
        {
            var metrics = new List<string>
            {
                $"pages: {row.PageCount}",
                $"visits: {row.VisitCount}"
            };
            if (request.Include.Contains("favorites", StringComparison.OrdinalIgnoreCase))
            {
                metrics.Add($"favorites: {row.FavoriteCount}");
            }

            if (request.Include.Contains("collections", StringComparison.OrdinalIgnoreCase))
            {
                metrics.Add($"collections: {row.CollectionLinkCount}");
            }

            metrics.Add($"last: {row.LastVisitedAt:g}");

            markdown.AppendLine($"- **{EscapeMarkdown(row.Period)}**  ");
            markdown.AppendLine($"  {string.Join(" | ", metrics)}");
        }

        if (report.Rows.Count > 12)
        {
            markdown.AppendLine($"- {report.Rows.Count - 12} more periods in this result.");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), BuildHistoryGroupHtmlReport(report));
    }

    public static BrowserDataAssistantResult BuildHistoryArchiveReport(string prompt)
    {
        if (!TryExtractHistoryPeriod(prompt, out var startedAt, out var endedAt, out var label))
        {
            return new BrowserDataAssistantResult("## Archive history\nTell me what to archive, like `archive history for June` or `archive history for 2025`.");
        }

        var summary = HistoryPersistenceService.ArchiveHistoryBetween(startedAt, endedAt, label);
        return new BrowserDataAssistantResult($"""
            ## History archived

            - Period: **{EscapeMarkdown(summary.Period)}**
            - Items archived: **{summary.ArchivedItemCount}**
            - Range: {summary.StartedAt:g} to {summary.EndedAt:g}
            """.Trim());
    }

    public static BrowserDataAssistantResult BuildFavoritesSummaryReport()
    {
        return BuildFavoritesReport("favorites summary");
    }

    public static BrowserDataAssistantResult BuildTabsSummaryReport()
    {
        var tabs = TabPersistenceService.LoadTabs<BrowserTab[]>("tabs") ?? [];
        var selectedTabId = TabPersistenceService.LoadTabs<string>("selectedTabId");
        var orderedTabs = tabs
            .OrderBy(tab => tab.Order)
            .ThenBy(tab => tab.DateTime)
            .ToArray();
        var selectedTab = orderedTabs.FirstOrDefault(tab => string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal));

        var markdown = new StringBuilder();
        markdown.AppendLine("## Saved tab session");
        markdown.AppendLine();
        markdown.AppendLine($"- Saved tabs: **{orderedTabs.Length}**");
        markdown.AppendLine($"- Selected tab: **{MarkdownLink(GetDisplayTitle(selectedTab), selectedTab?.Url)}**");
        markdown.AppendLine($"- Favorite tabs: **{orderedTabs.Count(tab => tab.IsFavorite)}**");
        markdown.AppendLine($"- Home tabs: **{orderedTabs.Count(tab => tab.IsHomeTab)}**");
        markdown.AppendLine($"- Sleeping tabs: **{orderedTabs.Count(tab => tab.IsSleeping)}**");
        markdown.AppendLine();

        if (orderedTabs.Length == 0)
        {
            markdown.AppendLine("No persisted tabs are stored right now. LinkScape will open the configured home page on startup.");
            return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
        }

        markdown.AppendLine("### Tabs");
        markdown.AppendLine();

        foreach (var tab in orderedTabs.Take(20))
        {
            var badges = new List<string>();

            if (string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal))
            {
                badges.Add("selected");
            }

            if (tab.IsFavorite)
            {
                badges.Add("favorite");
            }

            if (tab.IsHomeTab)
            {
                badges.Add("home");
            }

            if (tab.IsSleeping)
            {
                badges.Add("sleeping");
            }

            var suffix = badges.Count > 0 ? $" · {string.Join(", ", badges)}" : string.Empty;
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(tab), tab.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(tab.Url))} · visits: {tab.VisitedCount} · saved {tab.DateTime:g}{suffix}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
    }

    public static BrowserDataAssistantResult BuildTabsSearchReport(string query)
    {
        query = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return new BrowserDataAssistantResult("## Tab search\nTell me what to search for in your saved tabs.");
        }

        var tabs = (TabPersistenceService.LoadTabs<BrowserTab[]>("tabs") ?? [])
            .Where(tab =>
                tab.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                tab.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tab => tab.Order)
            .ThenBy(tab => tab.DateTime)
            .Take(12)
            .ToArray();

        if (tabs.Length == 0)
        {
            return new BrowserDataAssistantResult($"## Tab search\nNo saved tabs matched `{EscapeMarkdown(query)}`.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"## Saved tabs matching `{EscapeMarkdown(query)}`");
        markdown.AppendLine();

        foreach (var tab in tabs)
        {
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(tab), tab.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(tab.Url)}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
    }

    public static BrowserDataAssistantResult BuildCollectionsListReport()
    {
        var collections = TabCollectionService.GetCollections();
        var startupCollection = TabCollectionService.GetStartupCollection();
        var markdown = new StringBuilder();
        markdown.AppendLine("## Tab collections");
        markdown.AppendLine();
        markdown.AppendLine($"- Collections stored: **{collections.Count}**");
        markdown.AppendLine($"- Startup collection: **{EscapeMarkdown(startupCollection?.Name ?? "None")}**");
        markdown.AppendLine();

        if (collections.Count == 0)
        {
            markdown.AppendLine("No collections exist yet. Ask Linker to add the current page to a collection, for example `add current page to collection personal`.");
            return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
        }

        markdown.AppendLine("### Collections");
        markdown.AppendLine();

        foreach (var collection in collections)
        {
            var items = TabCollectionService.GetItems(collection.Id);
            var startupSuffix = startupCollection?.Id == collection.Id ? " · startup" : string.Empty;
            markdown.AppendLine($"- **{EscapeMarkdown(collection.Name)}** · items: {items.Count} · updated {collection.UpdatedAt:g}{startupSuffix}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
    }

    public static BrowserDataAssistantResult BuildCollectionSummaryReport(string collectionNameOrPrompt)
    {
        var collectionName = ExtractCollectionName(collectionNameOrPrompt);
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return BuildCollectionsListReport();
        }

        var collection = TabCollectionService.GetCollection(collectionName) ?? TabCollectionService.GetCollectionByName(collectionName);

        if (collection is null)
        {
            return new BrowserDataAssistantResult($"## Collection not found\nI could not find a collection named `{EscapeMarkdown(collectionName)}`.", null);
        }

        var items = TabCollectionService.GetItems(collection.Id);
        var startupCollection = TabCollectionService.GetStartupCollection();
        var markdown = new StringBuilder();
        markdown.AppendLine($"## {EscapeMarkdown(collection.Name)} collection");
        markdown.AppendLine();
        markdown.AppendLine($"- Items: **{items.Count}**");
        markdown.AppendLine($"- Startup collection: **{(startupCollection?.Id == collection.Id ? "yes" : "no")}**");
        markdown.AppendLine($"- Updated: **{collection.UpdatedAt:g}**");
        markdown.AppendLine();

        if (items.Count == 0)
        {
            markdown.AppendLine("This collection is empty.");
            return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
        }

        markdown.AppendLine("### Items");
        markdown.AppendLine();

        foreach (var item in items.Take(20))
        {
            markdown.AppendLine($"- **{MarkdownLink(item.Title, item.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(item.Url))} · updated {item.UpdatedAt:g}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
    }

    public static BrowserDataAssistantResult BuildAddCollectionItemReport(string collectionNameOrPrompt, string url, string? title)
    {
        var collectionName = ExtractCollectionName(collectionNameOrPrompt);
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return new BrowserDataAssistantResult("## Collection name needed\nTell me which collection to use, like `add current page to collection personal`.");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return new BrowserDataAssistantResult("## No active page to add\nI need a URL or the current active page context before I can add an item to a collection.");
        }

        var item = TabCollectionService.AddOrUpdateItem(collectionName, url, title);
        var collection = TabCollectionService.GetCollection(item.CollectionId);

        return new BrowserDataAssistantResult($"""
            ## Added to {EscapeMarkdown(collection?.Name ?? collectionName)}

            - Item: **{MarkdownLink(item.Title, item.Url)}**
            - Site: {EscapeMarkdown(GetHost(item.Url))}
            - Items in collection: **{TabCollectionService.GetItems(item.CollectionId).Count}**
            """.Trim());
    }

    public static BrowserDataAssistantResult BuildRemoveCollectionItemReport(string collectionNameOrPrompt, string url)
    {
        var collectionName = ExtractCollectionName(collectionNameOrPrompt);
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return new BrowserDataAssistantResult("## Collection name needed\nTell me which collection to use, like `remove current page from collection personal`.");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return new BrowserDataAssistantResult("## No page to remove\nI need a URL or the current active page context before I can remove an item from a collection.");
        }

        var collection = TabCollectionService.GetCollection(collectionName) ?? TabCollectionService.GetCollectionByName(collectionName);
        if (collection is null)
        {
            return new BrowserDataAssistantResult($"## Collection not found\nI could not find `{EscapeMarkdown(collectionName)}`.");
        }

        var beforeItems = TabCollectionService.GetItems(collection.Id);
        var removedItem = beforeItems.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        var removed = TabCollectionService.RemoveItem(collection.Id, url);
        var afterCount = TabCollectionService.GetItems(collection.Id).Count;

        return removed
            ? new BrowserDataAssistantResult($"""
                ## Removed from {EscapeMarkdown(collection.Name)}

                - Item: **{MarkdownLink(removedItem?.Title ?? url, removedItem?.Url ?? url)}**
                - Items in collection: **{afterCount}**
                """.Trim())
            : new BrowserDataAssistantResult($"""
                ## Item was not in {EscapeMarkdown(collection.Name)}

                - URL checked: **{MarkdownLink(url, url)}**
                - Items in collection: **{afterCount}**
                """.Trim());
    }

    public static BrowserDataAssistantResult BuildRenameCollectionReport(string currentNameOrPrompt, string nextName)
    {
        var (currentName, parsedNextName) = ExtractRenameCollectionNames(currentNameOrPrompt);
        var resolvedNextName = string.IsNullOrWhiteSpace(nextName) ? parsedNextName : nextName.Trim();

        if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(resolvedNextName))
        {
            return new BrowserDataAssistantResult("## Rename needs two names\nUse something like `rename collection personal to day off`.");
        }

        var renamed = TabCollectionService.RenameCollection(currentName, resolvedNextName);
        return renamed
            ? new BrowserDataAssistantResult($"## Collection renamed\n`{EscapeMarkdown(currentName)}` is now **{EscapeMarkdown(resolvedNextName)}**.")
            : new BrowserDataAssistantResult($"## Collection not found\nI could not find `{EscapeMarkdown(currentName)}`.", null);
    }

    public static BrowserDataAssistantResult BuildSetStartupCollectionReport(string collectionNameOrPrompt)
    {
        var collectionName = ExtractCollectionName(collectionNameOrPrompt);

        try
        {
            TabCollectionService.SetStartupCollection(collectionName);
        }
        catch (Exception ex)
        {
            return new BrowserDataAssistantResult($"## Startup collection not changed\n{EscapeMarkdown(ex.Message)}");
        }

        var collection = TabCollectionService.GetStartupCollection();
        return new BrowserDataAssistantResult($"""
            ## Startup collection set

            LinkScape will open **{EscapeMarkdown(collection?.Name ?? collectionName)}** when startup mode is set to collections.
            """.Trim());
    }

    public static BrowserDataAssistantResult BuildFavoritesSearchReport(string query)
    {
        var favorites = FavoritesService.SearchFavorites(query).Take(12).ToArray();

        if (favorites.Length == 0)
        {
            return new BrowserDataAssistantResult($"## Favorites search\nNo favorites matched `{query}`.");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"## Favorites matching `{EscapeMarkdown(query)}`");
        markdown.AppendLine();

        foreach (var favorite in favorites)
        {
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(favorite), favorite.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(favorite.Url)}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), BuildFavoritesHtmlReport(favorites));
    }

    private static BrowserDataAssistantResult BuildHistoryReport(string prompt)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var recent = HistoryPersistenceService.GetRecentHistory(200);
        var todayItems = recent
            .Where(item => item.LastVisitedAt.Date == today)
            .OrderByDescending(item => item.LastVisitedAt)
            .ToArray();
        var activeItems = todayItems.Length > 0 ? todayItems : recent.Take(25).ToArray();
        var totalVisits = activeItems.Sum(item => item.VisitCount);
        var topHosts = activeItems
            .GroupBy(item => GetHost(item.Url), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new
            {
                Host = group.Key,
                Items = group.Count(),
                Visits = group.Sum(item => item.VisitCount),
                LastVisitedAt = group.Max(item => item.LastVisitedAt)
            })
            .OrderByDescending(item => item.Visits)
            .ThenByDescending(item => item.LastVisitedAt)
            .Take(8)
            .ToArray();

        var markdown = new StringBuilder();
        markdown.AppendLine(todayItems.Length > 0 ? "## Today's browser activity" : "## Recent browser activity");
        markdown.AppendLine();
        markdown.AppendLine($"- Pages considered: **{activeItems.Length}**");
        markdown.AppendLine($"- Total stored visits across those pages: **{totalVisits}**");
        markdown.AppendLine($"- Most recent item: **{MarkdownLink(GetDisplayTitle(activeItems.FirstOrDefault()), activeItems.FirstOrDefault()?.Url)}**");
        markdown.AppendLine();

        if (topHosts.Length > 0)
        {
            markdown.AppendLine("### Top active sites");
            markdown.AppendLine();
            AppendTopSiteTable(markdown, topHosts.Select(item => (item.Host, item.Items, item.Visits, item.LastVisitedAt)).ToArray());
            markdown.AppendLine();
        }

        markdown.AppendLine("### Sample pages");
        markdown.AppendLine();

        foreach (var item in activeItems.Take(5))
        {
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(item), item.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(item.Url))} · {item.LastVisitedAt:g} · visits: {item.VisitCount}");
        }

        if (activeItems.Length > 5)
        {
            markdown.AppendLine($"- {activeItems.Length - 5} more pages in this result.");
        }

        var html = BuildHistoryHtmlReport(activeItems, topHosts.Select(item => (item.Host, item.Items, item.Visits, item.LastVisitedAt)).ToArray());
        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), html);
    }

    private static BrowserDataAssistantResult BuildHistoryReport(
        string title,
        IReadOnlyList<HistoryItem> activeItems,
        string? note = null)
    {
        var totalVisits = activeItems.Sum(item => item.VisitCount);
        var topHosts = activeItems
            .GroupBy(item => GetHost(item.Url), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new
            {
                Host = group.Key,
                Items = group.Count(),
                Visits = group.Sum(item => item.VisitCount),
                LastVisitedAt = group.Max(item => item.LastVisitedAt)
            })
            .OrderByDescending(item => item.Visits)
            .ThenByDescending(item => item.LastVisitedAt)
            .Take(8)
            .ToArray();

        var markdown = new StringBuilder();
        markdown.AppendLine($"## {title}");
        markdown.AppendLine();

        if (!string.IsNullOrWhiteSpace(note))
        {
            markdown.AppendLine($"- {note}");
        }

        markdown.AppendLine($"- Pages considered: **{activeItems.Count}**");
        markdown.AppendLine($"- Total stored visits across those pages: **{totalVisits}**");
        markdown.AppendLine($"- Most recent item: **{MarkdownLink(GetDisplayTitle(activeItems.FirstOrDefault()), activeItems.FirstOrDefault()?.Url)}**");
        markdown.AppendLine();

        if (topHosts.Length > 0)
        {
            markdown.AppendLine("### Top active sites");
            markdown.AppendLine();
            AppendTopSiteTable(markdown, topHosts.Select(item => (item.Host, item.Items, item.Visits, item.LastVisitedAt)).ToArray());
            markdown.AppendLine();
        }

        if (activeItems.Count > 0)
        {
            markdown.AppendLine("### Sample pages");
            markdown.AppendLine();

            foreach (var item in activeItems.Take(5))
            {
                markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(item), item.Url)}**  ");
                markdown.AppendLine($"  {EscapeMarkdown(GetHost(item.Url))} · {item.LastVisitedAt:g} · visits: {item.VisitCount}");
            }

            if (activeItems.Count > 5)
            {
                markdown.AppendLine($"- {activeItems.Count - 5} more pages in this result.");
            }
        }

        var html = BuildHistoryHtmlReport(activeItems, topHosts.Select(item => (item.Host, item.Items, item.Visits, item.LastVisitedAt)).ToArray());
        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), html);
    }

    private static BrowserDataAssistantResult BuildFavoritesReport(string prompt)
    {
        var favorites = FavoritesService.GetFavorites().Take(50).ToArray();
        var recentFavorites = favorites
            .OrderByDescending(item => item.UpdatedAt)
            .Take(10)
            .ToArray();

        var markdown = new StringBuilder();
        markdown.AppendLine("## Favorites summary");
        markdown.AppendLine();
        markdown.AppendLine($"- Favorites stored: **{favorites.Length}**");
        markdown.AppendLine($"- Most recently updated: **{MarkdownLink(GetDisplayTitle(recentFavorites.FirstOrDefault()), recentFavorites.FirstOrDefault()?.Url)}**");
        markdown.AppendLine();
        markdown.AppendLine("### Recent favorites");
        markdown.AppendLine();

        foreach (var favorite in recentFavorites)
        {
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(favorite), favorite.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(favorite.Url))} · updated {favorite.UpdatedAt:g}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd(), BuildFavoritesHtmlReport(recentFavorites));
    }

    private static string BuildHistoryHtmlReport(
        IReadOnlyList<HistoryItem> items,
        IReadOnlyList<(string Host, int Items, int Visits, DateTime LastVisitedAt)> topHosts)
    {
        var rows = new StringBuilder();

        foreach (var host in topHosts)
        {
            var width = Math.Clamp(host.Visits * 8, 16, 240);
            rows.AppendLine($"""
                <tr>
                    <td>{Html(host.Host)}</td>
                    <td>{host.Items}</td>
                    <td>{host.Visits}</td>
                    <td><div class="bar" style="width:{width}px"></div></td>
                    <td>{Html(host.LastVisitedAt.ToString("g"))}</td>
                </tr>
                """);
        }

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
                body { font-family: Segoe UI, sans-serif; margin: 18px; color: #f8fafc; background: #111827; }
                h1 { font-size: 20px; margin: 0 0 12px; }
                table { border-collapse: collapse; width: 100%; background: #1f2937; border-radius: 12px; overflow: hidden; }
                th, td { padding: 10px 12px; border-bottom: 1px solid #374151; text-align: left; }
                th { background: #0f172a; color: #bfdbfe; }
                .bar { height: 10px; border-radius: 999px; background: linear-gradient(90deg, #000, #2563eb, #f97316); }
                .muted { color: #cbd5e1; }
            </style>
            </head>
            <body>
                <h1>Browser activity report</h1>
                <p class="muted">Pages analyzed: {{items.Count}}</p>
                <table>
                    <thead><tr><th>Site</th><th>Pages</th><th>Visits</th><th>Chart</th><th>Last active</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                </table>
            </body>
            </html>
            """;
    }

    private static string BuildFavoritesHtmlReport(IReadOnlyList<FavoriteItem> favorites)
    {
        var rows = new StringBuilder();

        foreach (var favorite in favorites)
        {
            rows.AppendLine($"""
                <tr>
                    <td>{Html(favorite.Title)}</td>
                    <td>{Html(GetHost(favorite.Url))}</td>
                    <td>{Html(favorite.UpdatedAt.ToString("g"))}</td>
                </tr>
                """);
        }

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
                body { font-family: Segoe UI, sans-serif; margin: 18px; color: #f8fafc; background: #111827; }
                table { border-collapse: collapse; width: 100%; background: #1f2937; border-radius: 12px; overflow: hidden; }
                th, td { padding: 10px 12px; border-bottom: 1px solid #374151; text-align: left; }
                th { background: #0f172a; color: #fed7aa; }
            </style>
            </head>
            <body>
                <h1>Favorites report</h1>
                <table>
                    <thead><tr><th>Favorite</th><th>Site</th><th>Updated</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                </table>
            </body>
            </html>
            """;
    }

    private static void AppendTopSiteTable(
        StringBuilder markdown,
        IReadOnlyList<(string Host, int Items, int Visits, DateTime LastVisitedAt)> topHosts)
    {
        markdown.AppendLine("| Site | Pages | Visits | Last active |");
        markdown.AppendLine("|---|---:|---:|---|");

        foreach (var host in topHosts.Take(6))
        {
            markdown.AppendLine($"| {EscapeMarkdown(TrimCell(host.Host, 28))} | {host.Items} | {host.Visits} | {host.LastVisitedAt:g} |");
        }

        if (topHosts.Count > 6)
        {
            markdown.AppendLine($"| {topHosts.Count - 6} more |  |  |  |");
        }
    }

    private static string TrimCell(string value, int maxLength)
    {
        value = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 1)].TrimEnd() + "...";
    }

    private static string BuildHistoryGroupHtmlReport(HistoryGroupSqlReport report)
    {
        var rows = new StringBuilder();

        foreach (var row in report.Rows)
        {
            rows.AppendLine($"""
                <tr>
                    <td>{Html(row.Period)}</td>
                    <td>{row.PageCount}</td>
                    <td>{row.VisitCount}</td>
                    <td>{row.FavoriteCount}</td>
                    <td>{row.CollectionLinkCount}</td>
                    <td>{Html(row.LastVisitedAt?.ToString("g") ?? string.Empty)}</td>
                </tr>
                """);
        }

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
                body { font-family: Segoe UI, sans-serif; margin: 18px; color: #f8fafc; background: #111827; }
                h1 { font-size: 20px; margin: 0 0 8px; }
                .muted { color: #cbd5e1; margin: 0 0 14px; }
                table { border-collapse: collapse; width: 100%; background: #1f2937; border-radius: 12px; overflow: hidden; }
                th, td { padding: 10px 12px; border-bottom: 1px solid #374151; text-align: left; }
                th { background: #0f172a; color: #bfdbfe; }
                td:not(:first-child), th:not(:first-child) { text-align: right; }
                td:last-child, th:last-child { text-align: left; }
            </style>
            </head>
            <body>
                <h1>History grouped by {{Html(report.Request.GroupBy)}}</h1>
                <p class="muted">State: {{Html(report.Request.State)}}. Includes: {{Html(report.Request.Include)}}. Sort: {{Html(report.Request.SortBy)}}. URL: {{Html(report.Request.Url ?? "All")}}.</p>
                <table>
                    <thead><tr><th>Period</th><th>Pages</th><th>Visits</th><th>Favorites</th><th>Collections</th><th>Last visit</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                </table>
            </body>
            </html>
            """;
    }

    private static bool IsFavoritesPrompt(string prompt) =>
        prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase);

    private static string ExtractHistorySearchQuery(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var quoted = Regex.Match(prompt, "\"(?<query>[^\"]+)\"");
        if (quoted.Success)
        {
            return quoted.Groups["query"].Value.Trim();
        }

        var startsWith = Regex.Match(prompt, @"\bstarts?\s+with\s+(?<query>.+)$", RegexOptions.IgnoreCase);
        if (startsWith.Success)
        {
            return CleanHistorySearchQuery(startsWith.Groups["query"].Value);
        }

        var anythingWith = Regex.Match(prompt, @"\b(anything\s+with|with|named?|search(?:\s+for)?|find)\s+(?<query>.+)$", RegexOptions.IgnoreCase);
        if (anythingWith.Success)
        {
            return CleanHistorySearchQuery(anythingWith.Groups["query"].Value);
        }

        return CleanHistorySearchQuery(prompt);
    }

    private static string CleanHistorySearchQuery(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\b(show|me|anything|history|browser|search|find|for|with|in|from|the|name|named|title|url|that|contains|starts|start)\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '!', '?', '"', '\'');
    }

    private static string[] ExtractHistoryReportSearchTerms(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var quoted = Regex.Match(prompt, "\"(?<terms>[^\"]+)\"");
        var value = quoted.Success
            ? quoted.Groups["terms"].Value
            : Regex.Match(
                prompt,
                @"\b(?:about|for|with)\s+(?<terms>.+?)\s+(?:(?:\b(?:for|in|during)\s+)?(?:this\s+month|last\s+month|this\s+year|last\s+year|january|february|march|april|may|june|july|august|september|october|november|december|20\d{2}))\b",
                RegexOptions.IgnoreCase).Groups["terms"].Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        value = Regex.Replace(value, @"\band\b", ",", RegexOptions.IgnoreCase);
        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(term => Regex.Replace(term, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '!', '?', '"', '\''))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryExtractHistoryPeriod(string prompt, out DateTime startedAt, out DateTime endedAt, out string label)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var now = DateTime.Now;
        startedAt = DateTime.MinValue;
        endedAt = DateTime.MinValue;
        label = string.Empty;

        if (prompt.Contains("this month", StringComparison.OrdinalIgnoreCase))
        {
            startedAt = new DateTime(now.Year, now.Month, 1);
            endedAt = startedAt.AddMonths(1);
            label = startedAt.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        if (prompt.Contains("last month", StringComparison.OrdinalIgnoreCase))
        {
            endedAt = new DateTime(now.Year, now.Month, 1);
            startedAt = endedAt.AddMonths(-1);
            label = startedAt.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        if (prompt.Contains("this year", StringComparison.OrdinalIgnoreCase))
        {
            startedAt = new DateTime(now.Year, 1, 1);
            endedAt = startedAt.AddYears(1);
            label = startedAt.ToString("yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        if (prompt.Contains("last year", StringComparison.OrdinalIgnoreCase))
        {
            startedAt = new DateTime(now.Year - 1, 1, 1);
            endedAt = startedAt.AddYears(1);
            label = startedAt.ToString("yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        var monthMatch = Regex.Match(
            prompt,
            @"\b(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\b(?:\s+(?<year>20\d{2}))?",
            RegexOptions.IgnoreCase);
        if (monthMatch.Success)
        {
            var month = DateTime.ParseExact(monthMatch.Groups["month"].Value, "MMMM", CultureInfo.InvariantCulture).Month;
            var year = monthMatch.Groups["year"].Success
                ? int.Parse(monthMatch.Groups["year"].Value, CultureInfo.InvariantCulture)
                : now.Year;
            startedAt = new DateTime(year, month, 1);
            endedAt = startedAt.AddMonths(1);
            label = startedAt.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        var yearMatch = Regex.Match(prompt, @"\b(?<year>20\d{2})\b");
        if (yearMatch.Success)
        {
            var year = int.Parse(yearMatch.Groups["year"].Value, CultureInfo.InvariantCulture);
            startedAt = new DateTime(year, 1, 1);
            endedAt = startedAt.AddYears(1);
            label = startedAt.ToString("yyyy", CultureInfo.CurrentCulture);
            return true;
        }

        return false;
    }

    private static string GetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : url;
    }

    private static string GetDisplayTitle(HistoryItem? item)
    {
        if (item is null)
        {
            return "None";
        }

        var title = string.IsNullOrWhiteSpace(item.Title) ? string.Empty : item.Title.Trim();
        if (!string.IsNullOrWhiteSpace(title) &&
            !title.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !title.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
            var path = uri.AbsolutePath.Trim('/');
            return string.IsNullOrWhiteSpace(path)
                ? host
                : $"{host}/{path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}";
        }

        return item.Url;
    }

    private static string GetDisplayTitle(FavoriteItem? item) =>
        item is null ? "None" : string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;

    private static string GetDisplayTitle(BrowserTab? tab) =>
        tab is null ? "None" : string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;

    private static string EscapeMarkdown(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string MarkdownLink(string title, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return EscapeMarkdown(title);
        }

        return $"[{EscapeMarkdownLinkText(title)}](<{EscapeMarkdownLinkUrl(url)}>)";
    }

    private static string EscapeMarkdownLinkText(string value) =>
        EscapeMarkdown(value)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string EscapeMarkdownLinkUrl(string value) =>
        value.Replace(">", "%3E", StringComparison.Ordinal);

    private static string ExtractCollectionName(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var knownCollection = FindCollectionNameInPrompt(value);
        if (!string.IsNullOrWhiteSpace(knownCollection))
        {
            return knownCollection;
        }

        var match = Regex.Match(value, @"\bcollections?\s+(?<name>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return CleanCollectionName(match.Groups["name"].Value);
        }

        return CleanCollectionName(value);
    }

    private static string FindCollectionNameInPrompt(string value)
    {
        foreach (var collection in TabCollectionService.GetCollections())
        {
            if (Regex.IsMatch(value, $@"\b{Regex.Escape(collection.Name)}\b", RegexOptions.IgnoreCase))
            {
                return collection.Name;
            }
        }

        return string.Empty;
    }

    private static (string CurrentName, string NextName) ExtractRenameCollectionNames(string value)
    {
        value = value?.Trim() ?? string.Empty;
        var match = Regex.Match(value, @"\bcollections?\s+(?<current>.+?)\s+\bto\b\s+(?<next>.+)$", RegexOptions.IgnoreCase);
        return match.Success
            ? (CleanCollectionName(match.Groups["current"].Value), CleanCollectionName(match.Groups["next"].Value))
            : (CleanCollectionName(value), string.Empty);
    }

    private static string CleanCollectionName(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\b(add|save|put|remove|delete|current|page|active|from|to|in|into|inside|the|my|me|what|whats|what's|are|is|set|startup|launch|open|on|collection|collections|summary|show|list|rename|edit)\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim(' ', '.', ',', ':', ';', '!', '?', '"', '\'');
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
