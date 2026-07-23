using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LinkScape.Models;

public enum AddressSearchSource
{
    All,
    Tabs,
    History,
    Favorites,
    AiResults
}

public sealed record AddressSearchResult(
    string Key,
    AddressSearchSource Source,
    string Title,
    string Url,
    string Detail,
    string? TabId = null);

public static class AddressSearchService
{
    private const int DefaultResultLimit = 8;

    public static bool CanSearchAiResults =>
        LinkerAiCredentialService.HasApiKey(LinkerAiCredentialService.SelectedProvider.Id);

    internal static IReadOnlyList<AddressSearchResult> SearchLocal(
        string query,
        IReadOnlyList<BrowserTab> tabs,
        AddressSearchSource source = AddressSearchSource.All,
        int limit = DefaultResultLimit)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length < 2 || source == AddressSearchSource.AiResults)
        {
            return [];
        }

        var results = new List<AddressSearchResult>();

        if (source is AddressSearchSource.All or AddressSearchSource.Tabs)
        {
            var sourceLimit = source == AddressSearchSource.All ? 3 : limit;
            results.AddRange(tabs
                .Where(tab => Contains(tab.Title, query) || Contains(tab.Url, query))
                .OrderByDescending(tab => string.Equals(tab.Title, query, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(tab => tab.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(tab => tab.Order)
                .Take(sourceLimit)
                .Select(tab => new AddressSearchResult(
                    $"tab:{tab.Id}",
                    AddressSearchSource.Tabs,
                    DisplayTitle(tab.Title, tab.Url),
                    tab.Url,
                    "Tabs",
                    tab.Id)));
        }

        if (source is AddressSearchSource.All or AddressSearchSource.History)
        {
            var sourceLimit = source == AddressSearchSource.All ? 3 : limit;
            results.AddRange(HistoryPersistenceService.SearchHistory(query, sourceLimit)
                .Select(item => new AddressSearchResult(
                    $"history:{item.Url}",
                    AddressSearchSource.History,
                    DisplayTitle(item.Title, item.Url),
                    item.Url,
                    $"History › {GetHistoryPeriod(item.LastVisitedAt)}")));
        }

        if (source is AddressSearchSource.All or AddressSearchSource.Favorites)
        {
            var sourceLimit = source == AddressSearchSource.All ? 3 : limit;
            results.AddRange(FavoritesService.SearchFavorites(query)
                .Take(sourceLimit)
                .Select(item => new AddressSearchResult(
                    $"favorite:{item.Id}",
                    AddressSearchSource.Favorites,
                    DisplayTitle(item.Title, item.Url),
                    item.Url,
                    "Favorites")));
        }

        return results
            .DistinctBy(result => result.Key)
            .Take(Math.Clamp(limit, 1, 10))
            .ToArray();
    }

    public static async Task<IReadOnlyList<AddressSearchResult>> SearchAiResultsAsync(
        string query,
        int limit = DefaultResultLimit,
        CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length < 2 || !CanSearchAiResults)
        {
            return [];
        }

        var provider = LinkerAiCredentialService.SelectedProvider;
        var credential = LinkerAiCredentialService.GetCredential(provider.Id);
        if (credential is null)
        {
            return [];
        }

        var resultLimit = Math.Clamp(limit, 5, 10);
        if (!string.Equals(provider.Id, "openai", StringComparison.OrdinalIgnoreCase))
        {
            var completion = await LinkerAiChatService.SubmitAsync(
                $"Find up to {resultLimit} useful website results for: {query}. Return only a JSON array. Each item must contain title, url, and snippet strings.",
                context: null,
                cancellationToken: cancellationToken);
            if (completion.IsError)
            {
                throw new InvalidOperationException(CleanProviderError(completion.Text));
            }

            return ParseAiResults(completion.Text, resultLimit);
        }

        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(credential.Deployment) ? provider.DefaultModel : credential.Deployment,
            ["instructions"] = $"Search the web and return up to {resultLimit} useful results. Return only a JSON array. Each item must contain title, url, and snippet strings.",
            ["input"] = query,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "web_search",
                    ["search_context_size"] = "low"
                }
            },
            ["max_tool_calls"] = 3,
            ["max_output_tokens"] = 900
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI web search returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var root = JsonNode.Parse(responseText);
        var outputText = root?["output_text"]?.GetValue<string>() ??
            string.Join(
                "\n",
                root?["output"]?.AsArray()
                    .SelectMany(item => item?["content"]?.AsArray() ?? [])
                    .Select(content => content?["text"]?.GetValue<string>() ?? string.Empty)
                    .Where(text => !string.IsNullOrWhiteSpace(text)) ?? []);

        return ParseAiResults(outputText, resultLimit);
    }

    public static IReadOnlyList<AddressSearchResult> ParseAiResults(string? value, int limit = DefaultResultLimit)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = value.IndexOf('\n');
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            value = firstNewLine >= 0 && lastFence > firstNewLine
                ? value[(firstNewLine + 1)..lastFence].Trim()
                : value;
        }

        var jsonArrayStart = System.Text.RegularExpressions.Regex.Match(value, @"\[\s*\{");
        var firstArray = jsonArrayStart.Success ? jsonArrayStart.Index : -1;
        var lastArray = value.LastIndexOf(']');
        if (firstArray >= 0 && lastArray > firstArray)
        {
            value = value[firstArray..(lastArray + 1)];
        }

        try
        {
            var parsed = JsonNode.Parse(value);
            var items = parsed is JsonArray array
                ? array
                : parsed?["results"]?.AsArray();
            if (items is null)
            {
                return ParseMarkdownLinks(value, limit);
            }

            return items
                .Select((item, index) =>
                {
                    var url = item?["url"]?.GetValue<string>()?.Trim() ?? string.Empty;
                    var title = item?["title"]?.GetValue<string>()?.Trim() ?? string.Empty;
                    var snippet = item?["snippet"]?.GetValue<string>()?.Trim() ?? string.Empty;
                    return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                        ? new AddressSearchResult(
                            $"web:{index}:{url}",
                            AddressSearchSource.AiResults,
                            DisplayTitle(title, url),
                            url,
                            string.IsNullOrWhiteSpace(snippet) ? "Web" : snippet)
                        : null;
                })
                .Where(result => result is not null)
                .Cast<AddressSearchResult>()
                .Take(Math.Clamp(limit, 1, 10))
                .ToArray();
        }
        catch
        {
            return ParseMarkdownLinks(value, limit);
        }
    }

    private static IReadOnlyList<AddressSearchResult> ParseMarkdownLinks(string value, int limit)
    {
        return System.Text.RegularExpressions.Regex.Matches(
                value ?? string.Empty,
                @"\[(?<title>[^\]]+)\]\((?<url>https?://[^)\s]+)\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select((match, index) => new AddressSearchResult(
                $"ai:{index}:{match.Groups["url"].Value}",
                AddressSearchSource.AiResults,
                match.Groups["title"].Value.Trim(),
                match.Groups["url"].Value.Trim(),
                "AI result"))
            .Take(Math.Clamp(limit, 1, 10))
            .ToArray();
    }

    private static string CleanProviderError(string value)
    {
        value = value?.Trim() ?? string.Empty;
        value = System.Text.RegularExpressions.Regex.Replace(value, @"^#+\s*", string.Empty);
        return string.IsNullOrWhiteSpace(value)
            ? "The selected AI provider could not return URL results."
            : value;
    }

    private static bool Contains(string? value, string query) =>
        (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string DisplayTitle(string? title, string url) =>
        string.IsNullOrWhiteSpace(title) ? url : title.Trim();

    private static string GetHistoryPeriod(DateTime visitedAt) =>
        visitedAt.Date == DateTime.Today
            ? "Today"
            : visitedAt.Date == DateTime.Today.AddDays(-1)
                ? "Yesterday"
                : visitedAt.ToString("MMM d");
}
