using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record BrowserFavoritesImportSource(
    string BrowserName,
    string ProfileName,
    string ProfileLabel,
    string FilePath,
    string Engine);

public sealed record BrowserFavoritesImportSummary(
    int SourceCount,
    int ImportedItemCount,
    IReadOnlyList<string> ImportedSources);

public static class BrowserFavoritesImportService
{
    public static IReadOnlyList<BrowserFavoritesImportSource> DiscoverSources()
    {
        var sources = new List<BrowserFavoritesImportSource>();

        sources.AddRange(DiscoverChromiumSources());
        sources.AddRange(DiscoverFirefoxSources());

        return sources;
    }

    public static BrowserFavoritesImportSummary ImportAllFavorites()
    {
        return ImportFavorites(DiscoverSources());
    }

    public static BrowserFavoritesImportSummary ImportBrowserFavorites(string browserName)
    {
        if (string.IsNullOrWhiteSpace(browserName))
        {
            return new BrowserFavoritesImportSummary(0, 0, []);
        }

        return ImportFavorites(GetSourcesForBrowser(browserName));
    }

    public static IReadOnlyList<string> DiscoverProfiles(string browserName)
    {
        if (string.IsNullOrWhiteSpace(browserName))
        {
            return [];
        }

        return GetSourcesForBrowser(browserName)
            .Select(source => source.ProfileLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static BrowserFavoritesImportSummary ImportBrowserFavorites(string browserName, string profileName)
    {
        if (string.IsNullOrWhiteSpace(browserName) || string.IsNullOrWhiteSpace(profileName))
        {
            return new BrowserFavoritesImportSummary(0, 0, []);
        }

        var sources = GetSourcesForBrowser(browserName)
            .Where(source => string.Equals(source.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return ImportFavorites(sources);
    }

    private static BrowserFavoritesImportSource[] GetSourcesForBrowser(string browserName)
    {
        return DiscoverSources()
            .Where(source => string.Equals(source.BrowserName, browserName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static BrowserFavoritesImportSummary ImportFavorites(IReadOnlyList<BrowserFavoritesImportSource> sources)
    {
        var importedCount = 0;
        var importedSources = new List<string>();

        foreach (var source in sources)
        {
            var favorites = source.Engine switch
            {
                "Chromium" => ReadChromiumBookmarks(source.FilePath),
                "Firefox" => ReadFirefoxBookmarks(source.FilePath),
                _ => []
            };

            if (favorites.Count == 0)
            {
                continue;
            }

            foreach (var favorite in favorites)
            {
                FavoritesService.UpsertFavorite(CreateImportedFavoriteId(favorite.Url), favorite.Url, favorite.Title);
            }

            importedCount += favorites.Count;
            importedSources.Add($"{source.BrowserName} - {source.ProfileLabel}");
        }

        return new BrowserFavoritesImportSummary(
            importedSources.Count,
            importedCount,
            importedSources);
    }

    private static IReadOnlyList<BrowserFavoritesImportSource> DiscoverChromiumSources()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var browserRoots = new (string BrowserName, string RootPath)[]
        {
            ("Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
            ("Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine(localAppData, "Vivaldi", "User Data")),
            ("Opera", Path.Combine(roamingAppData, "Opera Software", "Opera Stable"))
        };

        var sources = new List<BrowserFavoritesImportSource>();

        foreach (var (browserName, rootPath) in browserRoots)
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            var profileLabels = GetChromiumProfileLabels(rootPath);

            var rootBookmarksPath = Path.Combine(rootPath, "Bookmarks");

            if (File.Exists(rootBookmarksPath))
            {
                sources.Add(new BrowserFavoritesImportSource(
                    browserName,
                    "Default",
                    GetProfileLabel(profileLabels, "Default"),
                    rootBookmarksPath,
                    "Chromium"));
                continue;
            }

            foreach (var profileDirectory in Directory.EnumerateDirectories(rootPath))
            {
                var profileName = Path.GetFileName(profileDirectory);

                if (!profileName.StartsWith("Default", StringComparison.OrdinalIgnoreCase) &&
                    !profileName.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bookmarksPath = Path.Combine(profileDirectory, "Bookmarks");

                if (File.Exists(bookmarksPath))
                {
                    sources.Add(new BrowserFavoritesImportSource(
                        browserName,
                        profileName,
                        GetProfileLabel(profileLabels, profileName),
                        bookmarksPath,
                        "Chromium"));
                }
            }
        }

        return sources;
    }

    private static IReadOnlyList<BrowserFavoritesImportSource> DiscoverFirefoxSources()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesRoot = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
        var profileLabels = GetFirefoxProfileLabels(Path.Combine(appData, "Mozilla", "Firefox", "profiles.ini"));

        if (!Directory.Exists(profilesRoot))
        {
            return [];
        }

        var sources = new List<BrowserFavoritesImportSource>();

        foreach (var profileDirectory in Directory.EnumerateDirectories(profilesRoot))
        {
            var profileName = Path.GetFileName(profileDirectory);
            var placesPath = Path.Combine(profileDirectory, "places.sqlite");

            if (File.Exists(placesPath))
            {
                sources.Add(new BrowserFavoritesImportSource(
                    "Firefox",
                    profileName,
                    GetProfileLabel(profileLabels, profileName),
                    placesPath,
                    "Firefox"));
            }
        }

        return sources;
    }

    private static Dictionary<string, string> GetChromiumProfileLabels(string rootPath)
    {
        var localStatePath = Path.Combine(rootPath, "Local State");

        if (!File.Exists(localStatePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var stream = File.OpenRead(localStatePath);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("profile", out var profileElement) ||
                !profileElement.TryGetProperty("info_cache", out var infoCacheElement) ||
                infoCacheElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var profileProperty in infoCacheElement.EnumerateObject())
            {
                if (profileProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var profileLabel = TryGetJsonString(profileProperty.Value, "shortcut_name")
                    ?? TryGetJsonString(profileProperty.Value, "name")
                    ?? profileProperty.Name;

                labels[profileProperty.Name] = profileLabel;
            }

            return labels;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> GetFirefoxProfileLabels(string profilesIniPath)
    {
        if (!File.Exists(profilesIniPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? currentName = null;
            string? currentPath = null;

            void CommitCurrentProfile()
            {
                if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(currentPath))
                {
                    return;
                }

                var profileKey = Path.GetFileName(currentPath.Replace('/', Path.DirectorySeparatorChar));

                if (!string.IsNullOrWhiteSpace(profileKey))
                {
                    labels[profileKey] = currentName;
                }
            }

            foreach (var line in File.ReadLines(profilesIniPath))
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal))
                {
                    CommitCurrentProfile();
                    currentName = null;
                    currentPath = null;
                    continue;
                }

                if (trimmedLine.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = trimmedLine[5..];
                    continue;
                }

                if (trimmedLine.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = trimmedLine[5..];
                }
            }

            CommitCurrentProfile();
            return labels;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetProfileLabel(IReadOnlyDictionary<string, string> profileLabels, string profileName)
    {
        if (profileLabels.TryGetValue(profileName, out var profileLabel) && !string.IsNullOrWhiteSpace(profileLabel))
        {
            return profileLabel;
        }

        return profileName;
    }

    private static string? TryGetJsonString(JsonElement jsonElement, string propertyName)
    {
        if (!jsonElement.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static IReadOnlyList<(string Url, string Title)> ReadChromiumBookmarks(string bookmarksPath)
    {
        try
        {
            using var stream = File.OpenRead(bookmarksPath);
            using var document = JsonDocument.Parse(stream);
            var bookmarks = new List<(string Url, string Title)>();

            if (document.RootElement.TryGetProperty("roots", out var roots))
            {
                foreach (var root in roots.EnumerateObject())
                {
                    CollectChromiumBookmarks(root.Value, bookmarks);
                }
            }

            return bookmarks;
        }
        catch
        {
            return [];
        }
    }

    private static void CollectChromiumBookmarks(JsonElement node, List<(string Url, string Title)> bookmarks)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("type", out var typeElement) &&
            string.Equals(typeElement.GetString(), "url", StringComparison.OrdinalIgnoreCase) &&
            node.TryGetProperty("url", out var urlElement))
        {
            var url = urlElement.GetString() ?? string.Empty;

            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                var title = node.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : url;
                bookmarks.Add((url, string.IsNullOrWhiteSpace(title) ? url : title));
            }
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectChromiumBookmarks(child, bookmarks);
        }
    }

    private static IReadOnlyList<(string Url, string Title)> ReadFirefoxBookmarks(string placesPath)
    {
        var tempPath = CopyToTempFile(placesPath);

        try
        {
            using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT p.url, COALESCE(b.title, p.title, p.url)
                FROM moz_bookmarks b
                JOIN moz_places p ON p.id = b.fk
                WHERE b.type = 1
                    AND p.url IS NOT NULL
                    AND p.url != ''
                ORDER BY b.dateAdded DESC;
                """;

            using var reader = cmd.ExecuteReader();
            var bookmarks = new List<(string Url, string Title)>();

            while (reader.Read())
            {
                var url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? url : reader.GetString(1);
                bookmarks.Add((url, string.IsNullOrWhiteSpace(title) ? url : title));
            }

            return bookmarks;
        }
        catch
        {
            return [];
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static string CreateImportedFavoriteId(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return "imported-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string CopyToTempFile(string sourcePath)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"linkscape-favorites-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");

        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
