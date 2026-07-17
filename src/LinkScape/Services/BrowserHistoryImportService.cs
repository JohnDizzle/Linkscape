using System.Globalization;
using System.IO;
using System.Text.Json;

public sealed record BrowserHistoryImportSource(
    string BrowserName,
    string ProfileName,
    string ProfileLabel,
    string FilePath,
    string Engine);

public sealed record BrowserHistoryImportSummary(
    int SourceCount,
    int ImportedItemCount,
    IReadOnlyList<string> ImportedSources);

public static class BrowserHistoryImportService
{
    public static IReadOnlyList<BrowserHistoryImportSource> DiscoverSources()
    {
        var sources = new List<BrowserHistoryImportSource>();

        sources.AddRange(DiscoverChromiumSources());
        sources.AddRange(DiscoverFirefoxSources());

        return sources;
    }

    public static BrowserHistoryImportSummary ImportAllHistory(int limitPerSource = 2_000)
    {
        return ImportHistory(DiscoverSources(), limitPerSource);
    }

    public static BrowserHistoryImportSummary ImportBrowserHistory(string browserName, int limitPerSource = 2_000)
    {
        if (string.IsNullOrWhiteSpace(browserName))
        {
            return new BrowserHistoryImportSummary(0, 0, []);
        }

        return ImportHistory(GetSourcesForBrowser(browserName), limitPerSource);
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

    public static BrowserHistoryImportSummary ImportBrowserHistory(
        string browserName,
        string profileName,
        int limitPerSource = 2_000)
    {
        if (string.IsNullOrWhiteSpace(browserName) || string.IsNullOrWhiteSpace(profileName))
        {
            return new BrowserHistoryImportSummary(0, 0, []);
        }

        var sources = GetSourcesForBrowser(browserName)
            .Where(source => string.Equals(source.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return ImportHistory(sources, limitPerSource);
    }

    private static BrowserHistoryImportSource[] GetSourcesForBrowser(string browserName)
    {
        return DiscoverSources()
            .Where(source => string.Equals(source.BrowserName, browserName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static BrowserHistoryImportSummary ImportHistory(
        IReadOnlyList<BrowserHistoryImportSource> sources,
        int limitPerSource)
    {
        var importedItems = new Dictionary<string, HistoryItem>(StringComparer.OrdinalIgnoreCase);
        var importedSources = new List<string>();

        foreach (var source in sources)
        {
            var items = source.Engine switch
            {
                "Chromium" => ReadChromiumHistory(source.FilePath, limitPerSource),
                "Firefox" => ReadFirefoxHistory(source.FilePath, limitPerSource),
                _ => []
            };

            if (items.Count == 0)
            {
                continue;
            }

            foreach (var item in items)
            {
                var normalizedUrl = StoredUrlNormalizer.Normalize(item.Url);
                if (string.IsNullOrWhiteSpace(normalizedUrl))
                {
                    continue;
                }

                var normalizedItem = item with
                {
                    Url = normalizedUrl,
                    Title = string.IsNullOrWhiteSpace(item.Title) ? normalizedUrl : item.Title
                };

                if (importedItems.TryGetValue(normalizedUrl, out var existingItem))
                {
                    importedItems[normalizedUrl] = existingItem with
                    {
                        Title = string.IsNullOrWhiteSpace(normalizedItem.Title) ? existingItem.Title : normalizedItem.Title,
                        FirstVisitedAt = existingItem.FirstVisitedAt <= normalizedItem.FirstVisitedAt ? existingItem.FirstVisitedAt : normalizedItem.FirstVisitedAt,
                        LastVisitedAt = existingItem.LastVisitedAt >= normalizedItem.LastVisitedAt ? existingItem.LastVisitedAt : normalizedItem.LastVisitedAt,
                        VisitCount = Math.Max(existingItem.VisitCount, normalizedItem.VisitCount)
                    };
                }
                else
                {
                    importedItems[normalizedUrl] = normalizedItem;
                }
            }

            importedSources.Add($"{source.BrowserName} - {source.ProfileLabel}");
        }

        if (importedItems.Count > 0)
        {
            HistoryPersistenceService.UpsertImportedHistory(importedItems.Values);
        }

        return new BrowserHistoryImportSummary(
            importedSources.Count,
            importedItems.Count,
            importedSources);
    }

    private static IReadOnlyList<BrowserHistoryImportSource> DiscoverChromiumSources()
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

        var sources = new List<BrowserHistoryImportSource>();

        foreach (var (browserName, rootPath) in browserRoots)
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            var profileLabels = GetChromiumProfileLabels(rootPath);

            if (File.Exists(Path.Combine(rootPath, "History")))
            {
                sources.Add(new BrowserHistoryImportSource(
                    browserName,
                    "Default",
                    GetProfileLabel(profileLabels, "Default"),
                    Path.Combine(rootPath, "History"),
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

                var historyPath = Path.Combine(profileDirectory, "History");

                if (File.Exists(historyPath))
                {
                    sources.Add(new BrowserHistoryImportSource(
                        browserName,
                        profileName,
                        GetProfileLabel(profileLabels, profileName),
                        historyPath,
                        "Chromium"));
                }
            }
        }

        return sources;
    }

    private static IReadOnlyList<BrowserHistoryImportSource> DiscoverFirefoxSources()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesRoot = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
        var profileLabels = GetFirefoxProfileLabels(Path.Combine(appData, "Mozilla", "Firefox", "profiles.ini"));

        if (!Directory.Exists(profilesRoot))
        {
            return [];
        }

        var sources = new List<BrowserHistoryImportSource>();

        foreach (var profileDirectory in Directory.EnumerateDirectories(profilesRoot))
        {
            var profileName = Path.GetFileName(profileDirectory);
            var historyPath = Path.Combine(profileDirectory, "places.sqlite");

            if (File.Exists(historyPath))
            {
                sources.Add(new BrowserHistoryImportSource(
                    "Firefox",
                    profileName,
                    GetProfileLabel(profileLabels, profileName),
                    historyPath,
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

    private static List<HistoryItem> ReadChromiumHistory(string historyPath, int limit)
    {
        var tempPath = CopyToTempFile(historyPath);

        try
        {
            using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT url, title, visit_count, last_visit_time
                FROM urls
                WHERE url IS NOT NULL
                    AND url != ''
                    AND hidden = 0
                ORDER BY last_visit_time DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            var items = new List<HistoryItem>();

            while (reader.Read())
            {
                var url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? url : reader.GetString(1);
                var visitCount = reader.IsDBNull(2) ? 1 : Math.Max(1, reader.GetInt32(2));
                var lastVisitedAt = reader.IsDBNull(3)
                    ? DateTime.Now
                    : FromChromiumTimestamp(reader.GetInt64(3));

                items.Add(new HistoryItem(
                    url,
                    string.IsNullOrWhiteSpace(title) ? url : title,
                    lastVisitedAt,
                    lastVisitedAt,
                    visitCount));
            }

            return items;
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

    private static List<HistoryItem> ReadFirefoxHistory(string placesPath, int limit)
    {
        var tempPath = CopyToTempFile(placesPath);

        try
        {
            using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT url, title, visit_count, last_visit_date
                FROM moz_places
                WHERE url IS NOT NULL
                    AND url != ''
                    AND last_visit_date IS NOT NULL
                ORDER BY last_visit_date DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            var items = new List<HistoryItem>();

            while (reader.Read())
            {
                var url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? url : reader.GetString(1);
                var visitCount = reader.IsDBNull(2) ? 1 : Math.Max(1, reader.GetInt32(2));
                var lastVisitedAt = reader.IsDBNull(3)
                    ? DateTime.Now
                    : FromFirefoxTimestamp(reader.GetInt64(3));

                items.Add(new HistoryItem(
                    url,
                    string.IsNullOrWhiteSpace(title) ? url : title,
                    lastVisitedAt,
                    lastVisitedAt,
                    visitCount));
            }

            return items;
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

    private static string CopyToTempFile(string sourcePath)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"linkscape-history-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");

        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    private static DateTime FromChromiumTimestamp(long value)
    {
        try
        {
            return DateTimeOffset
                .FromFileTime(value * 10)
                .LocalDateTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static DateTime FromFirefoxTimestamp(long value)
    {
        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(value / 1_000)
                .LocalDateTime;
        }
        catch
        {
            return DateTime.Now;
        }
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
