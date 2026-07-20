using System.Net;
using System.Text;
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

            var suffix = badges.Count > 0 ? $" · {string.Join(", ", badges)}" : string.Empty;
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(tab), tab.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(tab.Url))} · visits: {tab.VisitedCount} · saved {tab.DateTime:g}{suffix}");
        }

        return new BrowserDataAssistantResult(markdown.ToString().TrimEnd());
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
            markdown.AppendLine("| Site | Pages | Visits | Last active |");
            markdown.AppendLine("|---|---:|---:|---|");

            foreach (var host in topHosts)
            {
                markdown.AppendLine($"| {EscapeMarkdown(host.Host)} | {host.Items} | {host.Visits} | {host.LastVisitedAt:g} |");
            }

            markdown.AppendLine();
        }

        markdown.AppendLine("### Latest pages");
        markdown.AppendLine();

        foreach (var item in activeItems.Take(8))
        {
            markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(item), item.Url)}**  ");
            markdown.AppendLine($"  {EscapeMarkdown(GetHost(item.Url))} · {item.LastVisitedAt:g} · visits: {item.VisitCount}");
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
            markdown.AppendLine("| Site | Pages | Visits | Last active |");
            markdown.AppendLine("|---|---:|---:|---|");

            foreach (var host in topHosts)
            {
                markdown.AppendLine($"| {EscapeMarkdown(host.Host)} | {host.Items} | {host.Visits} | {host.LastVisitedAt:g} |");
            }

            markdown.AppendLine();
        }

        if (activeItems.Count > 0)
        {
            markdown.AppendLine("### Pages");
            markdown.AppendLine();

            foreach (var item in activeItems.Take(10))
            {
                markdown.AppendLine($"- **{MarkdownLink(GetDisplayTitle(item), item.Url)}**  ");
                markdown.AppendLine($"  {EscapeMarkdown(GetHost(item.Url))} · {item.LastVisitedAt:g} · visits: {item.VisitCount}");
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

    private static bool IsFavoritesPrompt(string prompt) =>
        prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase);

    private static string GetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : url;
    }

    private static string GetDisplayTitle(HistoryItem? item) =>
        item is null ? "None" : string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;

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

    private static string Html(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
