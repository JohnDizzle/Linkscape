using System.IO;
using System.Text;

namespace LinkScape.Services.Collections;

public sealed record CollectionShortcutInfo(
    string CollectionId,
    string CollectionName,
    string ShortcutPath,
    bool Exists,
    bool IsValid);

public static class CollectionShortcutService
{
    private const string ShortcutSuffix = " - LinkScape";
    private const string ShortcutExtension = ".url";

    public static CollectionShortcutInfo GetStatus(TabCollection collection)
    {
        try
        {
            GetStableIconPath();
            var shortcutPath = FindShortcutPath(collection) ?? GetShortcutPath(collection);
            var exists = File.Exists(shortcutPath);
            return new CollectionShortcutInfo(
                collection.Id,
                collection.Name,
                shortcutPath,
                exists,
                exists && HasExpectedTarget(shortcutPath, collection.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CollectionShortcutInfo(
                collection.Id,
                collection.Name,
                GetShortcutPath(collection),
                Exists: false,
                IsValid: false);
        }
    }

    public static IReadOnlyList<CollectionShortcutInfo> GetInstalledShortcuts(
        IEnumerable<TabCollection> collections) =>
        collections.Select(GetStatus).Where(shortcut => shortcut.Exists).ToArray();

    public static CollectionShortcutInfo CreateOrUpdate(TabCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
        {
            throw new InvalidOperationException("The Windows desktop folder is not available.");
        }

        var previousPaths = FindOwnedShortcutPaths(collection.Id).ToArray();
        var shortcutPath = GetShortcutPath(collection);
        WriteShortcutFile(shortcutPath, CreateActivationUri(collection.Id), GetStableIconPath());

        var status = GetStatus(collection);
        if (!status.IsValid)
        {
            throw new InvalidOperationException("Windows created the shortcut, but LinkScape could not validate it.");
        }


        foreach (var previousPath in previousPaths.Where(path =>
            !string.Equals(path, shortcutPath, StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(previousPath);
        }

        return status;
    }

    public static bool Remove(string collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return false;
        }

        var removed = false;
        foreach (var path in FindOwnedShortcutPaths(collectionId))
        {
            File.Delete(path);
            removed = true;
        }

        return removed;
    }

    internal static string CreateActivationUri(string collectionId) =>
        $"link2scape://collection/{Uri.EscapeDataString(collectionId)}?mode=append";

    internal static void WriteShortcutFile(string shortcutPath, string activationUri, string iconPath)
    {
        var temporaryPath = $"{shortcutPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(
                temporaryPath,
                [
                    "[InternetShortcut]",
                    $"URL={activationUri}",
                    $"IconFile={iconPath}",
                    "IconIndex=0"
                ],
                Encoding.Unicode);
            File.Move(temporaryPath, shortcutPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool HasExpectedTarget(string shortcutPath, string collectionId)
    {
        return TryReadShortcut(shortcutPath, out var activationUri, out var iconPath) &&
            string.Equals(activationUri, CreateActivationUri(collectionId), StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(iconPath) &&
            File.Exists(iconPath);
    }

    private static string? FindShortcutPath(TabCollection collection) =>
        FindOwnedShortcutPaths(collection.Id).FirstOrDefault();

    private static IEnumerable<string> FindOwnedShortcutPaths(string collectionId)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(desktopPath, $"Start *{ShortcutSuffix}{ShortcutExtension}", SearchOption.TopDirectoryOnly)
                .Where(path => TryReadShortcut(path, out var activationUri, out _) &&
                    string.Equals(activationUri, CreateActivationUri(collectionId), StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string GetShortcutPath(TabCollection collection)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var fileName = $"Start {SanitizeFileName(collection.Name)}{ShortcutSuffix}{ShortcutExtension}";
        return Path.Combine(desktopPath, fileName);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Collection" : sanitized;
    }

    internal static bool TryReadShortcut(string shortcutPath, out string activationUri, out string iconPath)
    {
        activationUri = string.Empty;
        iconPath = string.Empty;
        try
        {
            foreach (var line in File.ReadLines(shortcutPath, Encoding.Unicode))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    activationUri = line[4..].Trim();
                }
                else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    iconPath = line[9..].Trim();
                }
            }

            return !string.IsNullOrWhiteSpace(activationUri);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string GetStableIconPath()
    {
        var iconFolder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "CollectionShortcuts");
        var iconPath = Path.Combine(iconFolder, "LinkScape.ico");
        Directory.CreateDirectory(iconFolder);

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.ico");
        if (File.Exists(sourcePath) &&
            (!File.Exists(iconPath) || new FileInfo(sourcePath).Length != new FileInfo(iconPath).Length))
        {
            File.Copy(sourcePath, iconPath, overwrite: true);
        }

        return File.Exists(iconPath) ? iconPath : Environment.ProcessPath ?? string.Empty;
    }
}
