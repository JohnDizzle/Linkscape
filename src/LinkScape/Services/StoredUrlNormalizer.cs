public static class StoredUrlNormalizer
{
    private static readonly HashSet<string> VolatileQueryParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_",
        "code",
        "client-request-id",
        "fbclid",
        "msclkid",
        "nonce",
        "redirectid",
        "session_state",
        "state"
    };

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();

        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.IsFile)
        {
            return null;
        }

        if (TryNormalizeYouTubeUrl(uri, out var normalizedYouTubeUrl))
        {
            return normalizedYouTubeUrl;
        }

        var filteredQueryParameters = ParseQueryString(uri.Query)
            .Where(static kvp => !IsVolatileParameter(kvp.Key))
            .OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static kvp => kvp.Value, StringComparer.Ordinal)
            .ToArray();

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = BuildQueryString(filteredQueryParameters)
        };

        var normalizedUrl = builder.Uri.AbsoluteUri;
        return builder.Path == "/"
            ? normalizedUrl.TrimEnd('/')
            : normalizedUrl;
    }

    private static bool TryNormalizeYouTubeUrl(Uri uri, out string? normalizedUrl)
    {
        normalizedUrl = null;

        if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                normalizedUrl = $"https://www.youtube.com/watch?v={videoId}";
                return true;
            }

            return false;
        }

        if (!uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, "/watch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQueryString(uri.Query);
        if (!query.TryGetValue("v", out var canonicalVideoId) || string.IsNullOrWhiteSpace(canonicalVideoId))
        {
            return false;
        }

        normalizedUrl = $"https://www.youtube.com/watch?v={canonicalVideoId}";
        return true;
    }

    private static bool IsVolatileParameter(string key)
    {
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || VolatileQueryParameterNames.Contains(key);
    }

    private static IReadOnlyDictionary<string, string> ParseQueryString(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1])
                : string.Empty;

            values[key] = value;
        }

        return values;
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> queryParameters)
    {
        return string.Join(
            "&",
            queryParameters.Select(static kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
    }
}