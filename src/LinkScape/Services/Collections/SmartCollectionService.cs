namespace LinkScape.Services.Collections;

public sealed record SmartCollectionSummary(
    int CollectionCount,
    int ItemCount,
    bool Rebuilt,
    IReadOnlyDictionary<string, int> CollectionItemCounts);

public static class SmartCollectionService
{
    public const string DefaultPrefix = "Smart - ";
    public const int DefaultItemsPerCollection = 10;
    public const int MaximumItemsPerCollection = 10;

    private sealed record Candidate(
        string Url,
        string Title,
        string Host,
        int VisitCount,
        DateTime LastUsedAt,
        bool IsFavorite,
        bool IsHistory);

    private sealed record Category(
        string Name,
        string[] Patterns);

    private static readonly Category[] Categories =
    [
        new("AI & Research", ["openai", "chatgpt", "copilot", "anthropic", "claude", "huggingface", "arxiv", "paper", "llm", "model", @"\bai\b", "generative-ai"]),
        new("Dev & Docs", ["github", "gitlab", "stackoverflow", "stackexchange", @"docs\.", "developer", "dotnet", "nuget", "npmjs", "react", "typescript", "powershell"]),
        new("Microsoft & Cloud", ["microsoft", "azure", @"portal\.azure", @"learn\.microsoft", "windows", "office", "copilotstudio"]),
        new("News & Reading", ["news", "msn", "cnn", "foxnews", "nbcnews", "cbsnews", "abcnews", "nytimes", "washingtonpost", "theverge", "tmz", "aol", "yahoo"]),
        new("Media & Communities", ["youtube", @"youtu\.be", @"music\.youtube", "twitch", "spotify", "netflix", "hulu", "disneyplus", "imdb", "rottentomatoes", "reddit", "tiktok", "instagram", "discord", @"x\.com"])
    ];

    public static SmartCollectionSummary CreateOrRefresh(
        bool rebuild = false,
        int historyLimit = 600,
        int itemsPerCollection = DefaultItemsPerCollection,
        string collectionPrefix = DefaultPrefix)
    {
        HistoryPersistenceService.EnsureDatabase();
        FavoritesService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();

        var candidates = BuildCandidates(historyLimit);
        if (rebuild)
        {
            DeleteGeneratedCollections(collectionPrefix);
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalItems = 0;

        foreach (var category in Categories)
        {
            var collectionName = $"{collectionPrefix}{category.Name}";
            var itemLimit = Math.Clamp(itemsPerCollection, 1, MaximumItemsPerCollection);
            var items = candidates
                .Where(candidate => IsMatch(candidate, category.Patterns))
                .OrderByDescending(GetScore)
                .ThenByDescending(candidate => candidate.LastUsedAt)
                .Take(itemLimit)
                .ToArray();

            // A Smart Collection is a ranked snapshot, not an append-only list.
            // Clear the previous generated contents so lowering the limit or
            // changing the ranking cannot leave stale items behind.
            foreach (var existingItem in TabCollectionService.GetItems(collectionName).ToArray())
            {
                TabCollectionService.RemoveItem(collectionName, existingItem.Url);
            }

            if (items.Length == 0)
            {
                counts[collectionName] = 0;
                continue;
            }

            TabCollectionService.UpsertCollection(collectionName);
            foreach (var item in items)
            {
                TabCollectionService.AddOrUpdateItem(collectionName, item.Url, item.Title);
            }

            counts[collectionName] = TabCollectionService.GetItems(collectionName).Count;
            totalItems += items.Length;
        }

        return new SmartCollectionSummary(
            Categories.Length,
            totalItems,
            rebuild,
            counts);
    }

    private static Candidate[] BuildCandidates(int historyLimit)
    {
        var byUrl = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in HistoryPersistenceService.GetMostVisited(Math.Max(1, historyLimit)))
        {
            if (!IsUsableUrl(item.Url, item.Title))
            {
                continue;
            }

            byUrl[item.Url] = new Candidate(
                item.Url,
                string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title,
                GetHost(item.Url),
                item.VisitCount,
                item.LastVisitedAt,
                IsFavorite: false,
                IsHistory: true);
        }

        foreach (var favorite in FavoritesService.GetFavorites())
        {
            if (!IsUsableUrl(favorite.Url, favorite.Title))
            {
                continue;
            }

            if (byUrl.TryGetValue(favorite.Url, out var existing))
            {
                byUrl[favorite.Url] = existing with
                {
                    Title = string.IsNullOrWhiteSpace(favorite.Title) ? existing.Title : favorite.Title,
                    LastUsedAt = existing.LastUsedAt > favorite.UpdatedAt ? existing.LastUsedAt : favorite.UpdatedAt,
                    IsFavorite = true
                };
                continue;
            }

            byUrl[favorite.Url] = new Candidate(
                favorite.Url,
                string.IsNullOrWhiteSpace(favorite.Title) ? favorite.Url : favorite.Title,
                GetHost(favorite.Url),
                0,
                favorite.UpdatedAt,
                IsFavorite: true,
                IsHistory: false);
        }

        return byUrl.Values.ToArray();
    }

    private static void DeleteGeneratedCollections(string collectionPrefix)
    {
        foreach (var collection in TabCollectionService.GetCollections()
            .Where(collection => collection.Name.StartsWith(collectionPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            TabCollectionService.DeleteCollection(collection.Id);
        }
    }

    private static bool IsMatch(Candidate candidate, string[] patterns)
    {
        var haystack = $"{candidate.Host} {candidate.Url}".ToLowerInvariant();
        return patterns.Any(pattern =>
            System.Text.RegularExpressions.Regex.IsMatch(
                haystack,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static double GetScore(Candidate candidate)
    {
        var daysOld = Math.Max(0, (DateTime.Now - candidate.LastUsedAt).TotalDays);
        var recencyBoost = Math.Max(0, 30 - daysOld) / 30;
        var favoriteBoost = candidate.IsFavorite ? 30 : 0;
        var bothSourceBoost = candidate.IsFavorite && candidate.IsHistory ? 12 : 0;
        return (candidate.VisitCount * 10) + favoriteBoost + bothSourceBoost + (recencyBoost * 8);
    }

    private static bool IsUsableUrl(string url, string title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var text = $"{host} {url} {title}".ToLowerInvariant();
        string[] blockedHosts =
        [
            "accounts.google.com",
            "login.microsoftonline.com",
            "login.live.com",
            "oauth.telegram.org"
        ];
        string[] blockedPatterns =
        [
            "/signin",
            "/login",
            "/oauth",
            "/authorize",
            "/auth/login",
            "two-step verification",
            "2-step verification",
            "passkey",
            "select_account"
        ];

        return !blockedHosts.Contains(host, StringComparer.OrdinalIgnoreCase) &&
            !blockedPatterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;
}
