namespace LinkScape.Browser;

internal sealed record BrowserSearchProvider(string Key, string DisplayName, string SearchUrlTemplate, string HomeUrl);

internal static class BrowserSearchProviders
{
    public const string DefaultProviderKey = "bing";

    private static readonly BrowserSearchProvider[] _providers =
    [
        new("bing", "Bing", "https://www.bing.com/search?q={0}", "https://www.bing.com/"),
        new("google", "Google", "https://www.google.com/search?q={0}", "https://www.google.com/"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={0}", "https://duckduckgo.com/"),
        new("ecosia", "Ecosia", "https://www.ecosia.org/search?q={0}", "https://www.ecosia.org/"),
        new("brave", "Brave", "https://search.brave.com/search?q={0}", "https://search.brave.com/")
    ];

    public static IReadOnlyList<BrowserSearchProvider> Providers => _providers;

    public static BrowserSearchProvider Default => GetByKey(DefaultProviderKey);

    public static string GetFaviconUrl(string? providerKey)
    {
        var provider = GetByKey(providerKey);
        var providerUri = new Uri(provider.SearchUrlTemplate, UriKind.Absolute);
        return $"https://www.google.com/s2/favicons?sz=32&domain={Uri.EscapeDataString(providerUri.Host)}";
    }

    public static BrowserSearchProvider GetByKey(string? providerKey)
    {
        return _providers.FirstOrDefault(p => string.Equals(p.Key, providerKey, StringComparison.OrdinalIgnoreCase))
            ?? _providers[0];
    }

    public static string NormalizeProviderKey(string? providerKey)
    {
        return GetByKey(providerKey).Key;
    }

    public static string BuildSearchUrl(string query, string? providerKey = null)
    {
        var provider = GetByKey(providerKey);

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            provider.SearchUrlTemplate,
            Uri.EscapeDataString((query ?? string.Empty).Trim()));
    }

    public static string GetHomeUrl(string? providerKey = null)
    {
        return GetByKey(providerKey).HomeUrl;
    }
}
