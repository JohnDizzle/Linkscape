using System.IO;

public sealed record ReactorReferenceStatus(
    string SourcePath,
    bool SourceExists,
    bool IsGitClone,
    string Message);

public sealed record ReactorReferenceSearchResult(
    string RelativePath,
    int LineNumber,
    string Preview);

public static class ReactorReferenceService
{
    public const string LocalSourcePath = @"C:\win_Reactor\microsoft-ui-reactor";
    private const int DefaultMaxResults = 8;
    private static readonly string[] SearchPatterns = ["*.cs", "*.xaml", "*.md", "*.csproj"];

    public static ReactorReferenceStatus GetStatus()
    {
        var sourceExists = Directory.Exists(LocalSourcePath);
        var isGitClone = Directory.Exists(Path.Combine(LocalSourcePath, ".git"));
        var message = (sourceExists, isGitClone) switch
        {
            (false, _) => "Local Reactor source is not available on this machine.",
            (true, false) => "Local Reactor source exists, but it is not a Git clone.",
            _ => "Local Reactor Git clone is available for offline API reference."
        };

        return new ReactorReferenceStatus(LocalSourcePath, sourceExists, isGitClone, message);
    }

    public static IReadOnlyList<ReactorReferenceSearchResult> Search(string query, int maxResults = DefaultMaxResults)
    {
        if (string.IsNullOrWhiteSpace(query) || !Directory.Exists(LocalSourcePath))
        {
            return [];
        }

        maxResults = Math.Clamp(maxResults, 1, 25);
        var results = new List<ReactorReferenceSearchResult>(maxResults);

        foreach (var file in EnumerateSearchFiles())
        {
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ReactorReferenceSearchResult(
                        Path.GetRelativePath(LocalSourcePath, file),
                        lineNumber,
                        line.Trim()));

                    if (results.Count >= maxResults)
                    {
                        return results;
                    }
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateSearchFiles()
    {
        foreach (var pattern in SearchPatterns)
        {
            foreach (var file in Directory.EnumerateFiles(LocalSourcePath, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
