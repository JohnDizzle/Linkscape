using LinkScape.Models;
using Microsoft.Web.WebView2.Core;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkScape.Services.WebApps;

public static class WebAppManifestService
{
    private const string FindManifestScript = """
        (() => {
            try {
                const manifest = document.querySelector('link[rel~="manifest"]');
                if (!manifest || !manifest.href) return null;
                return JSON.stringify({
                    manifestUrl: new URL(manifest.href, document.baseURI).href,
                    pageUrl: location.href
                });
            } catch {
                return null;
            }
        })();
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<InstallableWebApp?> DetectAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);

        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var pageUri) ||
            pageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            var result = await core.ExecuteScriptAsync(FindManifestScript);
            if (string.IsNullOrWhiteSpace(result) || result == "null")
            {
                return null;
            }

            var innerJson = JsonSerializer.Deserialize<string>(result);
            if (string.IsNullOrWhiteSpace(innerJson))
            {
                return null;
            }

            var discovery = JsonSerializer.Deserialize<ManifestDiscoveryResult>(innerJson, JsonOptions);
            if (discovery is null ||
                !Uri.TryCreate(discovery.ManifestUrl, UriKind.Absolute, out var manifestUri) ||
                manifestUri.Scheme is not ("http" or "https"))
            {
                return null;
            }

            return await LoadManifestAsync(pageUri, manifestUri, core.DocumentTitle);
        }
        catch
        {
            // PWA detection is optional and must never break normal browsing.
            return null;
        }
    }

    private static async Task<InstallableWebApp?> LoadManifestAsync(
        Uri pageUri,
        Uri manifestUri,
        string? documentTitle)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LinkScape/1.0");

        var json = await client.GetStringAsync(manifestUri);
        var manifest = JsonSerializer.Deserialize<WebAppManifest>(json, JsonOptions);
        if (manifest is null)
        {
            return null;
        }

        var startUrl = ResolveUri(manifestUri, manifest.StartUrl) ?? pageUri;
        if (startUrl.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var scope = ResolveUri(manifestUri, manifest.Scope) ?? GetDefaultScope(startUrl);
        if (!SameOrigin(startUrl, scope))
        {
            scope = GetDefaultScope(startUrl);
        }

        var name = manifest.Name ?? manifest.ShortName ?? documentTitle ?? pageUri.Host;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new InstallableWebApp
        {
            PageUrl = pageUri.AbsoluteUri,
            ManifestUrl = manifestUri.AbsoluteUri,
            Name = name.Trim(),
            ShortName = manifest.ShortName,
            StartUrl = startUrl.AbsoluteUri,
            Scope = scope.AbsoluteUri,
            IconUrl = SelectBestIcon(manifestUri, manifest.Icons)?.AbsoluteUri,
            ThemeColor = manifest.ThemeColor,
            DisplayMode = NormalizeDisplayMode(manifest.Display)
        };
    }

    private static Uri? ResolveUri(Uri baseUri, string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(baseUri, value, out var result)
            ? result
            : null;
    }

    private static Uri GetDefaultScope(Uri startUrl)
    {
        var builder = new UriBuilder(startUrl) { Query = string.Empty, Fragment = string.Empty };
        var path = builder.Path;
        var lastSlash = path.LastIndexOf('/');
        builder.Path = lastSlash >= 0 ? path[..(lastSlash + 1)] : "/";
        return builder.Uri;
    }

    private static bool SameOrigin(Uri first, Uri second) =>
        string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.Host, second.Host, StringComparison.OrdinalIgnoreCase) &&
        first.Port == second.Port;

    private static Uri? SelectBestIcon(Uri manifestUri, IReadOnlyList<WebAppManifestIcon> icons)
    {
        foreach (var icon in icons
                     .Where(icon => !string.IsNullOrWhiteSpace(icon.Src))
                     .OrderByDescending(icon => ParseLargestSize(icon.Sizes)))
        {
            if (Uri.TryCreate(manifestUri, icon.Src, out var iconUri) &&
                iconUri.Scheme is "http" or "https")
            {
                return iconUri;
            }
        }

        return null;
    }

    private static int ParseLargestSize(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes)) return 0;

        var largest = 0;
        foreach (var item in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = item.Split('x', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 2 && int.TryParse(pieces[0], out var width))
            {
                largest = Math.Max(largest, width);
            }
        }

        return largest;
    }

    private static string NormalizeDisplayMode(string? display) =>
        display?.ToLowerInvariant() switch
        {
            "fullscreen" => "fullscreen",
            "minimal-ui" => "minimal-ui",
            "standalone" => "standalone",
            _ => "standalone"
        };

    private sealed class ManifestDiscoveryResult
    {
        public string ManifestUrl { get; set; } = string.Empty;
        public string PageUrl { get; set; } = string.Empty;
    }
}
