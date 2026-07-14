using System.Globalization;
using System.IO;

public sealed record BrowserHistoryImportSource(
    string BrowserName,
    string ProfileName,
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

        var sources = DiscoverSources()
            .Where(source => string.Equals(source.BrowserName, browserName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return ImportHistory(sources, limitPerSource);
    }

    private static BrowserHistoryImportSummary ImportHistory(
        IReadOnlyList<BrowserHistoryImportSource> sources,
        int limitPerSource)
    {
        var importedItems = new List<HistoryItem>();
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

            importedItems.AddRange(items);
            importedSources.Add($"{source.BrowserName} - {source.ProfileName}");
        }

        if (importedItems.Count > 0)
        {
            HistoryPersistenceService.UpsertImportedHistory(importedItems);
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

            if (File.Exists(Path.Combine(rootPath, "History")))
            {
                sources.Add(new BrowserHistoryImportSource(browserName, "Default", Path.Combine(rootPath, "History"), "Chromium"));
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
                    sources.Add(new BrowserHistoryImportSource(browserName, profileName, historyPath, "Chromium"));
                }
            }
        }

        return sources;
    }

    private static IReadOnlyList<BrowserHistoryImportSource> DiscoverFirefoxSources()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesRoot = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");

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
                sources.Add(new BrowserHistoryImportSource("Firefox", profileName, historyPath, "Firefox"));
            }
        }

        return sources;
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
