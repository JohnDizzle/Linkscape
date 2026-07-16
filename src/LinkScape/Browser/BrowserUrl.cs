using System.Net;

namespace LinkScape.Browser;

internal static class BrowserUrl
{
    public static string Normalize(string raw, string fallback, string? providerKey = null)
    {
        var input = (raw ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        if (TryNormalizeAbsoluteUrl(input, out var normalizedUrl))
        {
            return normalizedUrl;
        }

        return BuildSearchUrl(input, providerKey);
    }

    public static bool AreEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        if (!Uri.TryCreate(left, UriKind.Absolute, out var lUri))
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(right, UriKind.Absolute, out var rUri))
        {
            return false;
        }

        return Uri.Compare(
            lUri,
            rUri,
            UriComponents.AbsoluteUri,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    public static string GetDomainFaviconUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return $"https://www.google.com/s2/favicons?sz=32&domain={Uri.EscapeDataString(uri.Host)}";
        }

        return "https://www.google.com/s2/favicons?sz=32&domain=bing.com";
    }

    private static bool TryNormalizeAbsoluteUrl(string input, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (Uri.TryCreate(input, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.IsFile || !string.IsNullOrWhiteSpace(absoluteUri.Host)))
        {
            normalizedUrl = absoluteUri.ToString();
            return true;
        }

        if (input.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        if (!LooksLikeUrl(input))
        {
            return false;
        }

        var candidate = input.Contains("://", StringComparison.Ordinal)
            ? input
            : $"https://{input}";

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri) &&
            !string.IsNullOrWhiteSpace(candidateUri.Host))
        {
            normalizedUrl = candidateUri.ToString();
            return true;
        }

        return false;
    }

    private static bool LooksLikeUrl(string input)
    {
        if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(input, out _))
        {
            return true;
        }

        var slashIndex = input.IndexOf('/');
        var hostCandidate = slashIndex >= 0 ? input[..slashIndex] : input;

        return hostCandidate.Contains('.', StringComparison.Ordinal);
    }

    private static string BuildSearchUrl(string query, string? providerKey)
    {
        return BrowserSearchProviders.BuildSearchUrl(query, providerKey);
    }
}