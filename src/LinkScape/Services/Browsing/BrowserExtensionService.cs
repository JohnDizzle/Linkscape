using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape.Services.Browsing;

internal sealed record BrowserExtensionDefinition(
    string Id,
    string DisplayName,
    string Description,
    string SettingKey,
    string? BundledFolderName,
    string BundledVersion,
    string License,
    string ProjectUrl)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(BundledFolderName);
}

internal static class BrowserExtensionService
{
    private static readonly SemaphoreSlim ExtensionOperationGate = new(1, 1);
    private const string RetiredDarkReaderInstalledIdKey =
        "extensions.dark-reader.installedId";

    public static readonly IReadOnlyList<BrowserExtensionDefinition> Extensions =
    [
        new("ublock-origin-lite", "uBlock Origin Lite",
            "Blocks ads and trackers with Manifest V3 rules.",
            "extensions.ublockOriginLite.enabled", "uBlockOriginLite",
            "2026.723.1724", "GPL-3.0",
            "https://github.com/uBlockOrigin/uBOL-home")
    ];

    public static async Task SetEnabledAsync(
        CoreWebView2Profile profile,
        BrowserExtensionDefinition definition,
        bool enabled)
    {
        await ExtensionOperationGate.WaitAsync();
        try
        {
            if (!definition.IsAvailable)
            {
                throw new InvalidOperationException($"{definition.DisplayName} is not bundled yet.");
            }

            var installed = await profile.GetBrowserExtensionsAsync();
            var extensionId = SettingsService.GetValue(GetInstalledIdSettingKey(definition));
            var extension = installed.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, extensionId, StringComparison.Ordinal));
            var installedBundleVersion =
                SettingsService.GetValue(GetInstalledBundleVersionSettingKey(definition));

            if (extension is not null &&
                !string.Equals(
                    installedBundleVersion,
                    definition.BundledVersion,
                    StringComparison.Ordinal))
            {
                await extension.RemoveAsync();
                extension = null;
                SettingsService.RemoveValue(GetInstalledIdSettingKey(definition));
                SettingsService.RemoveValue(GetInstalledBundleVersionSettingKey(definition));
            }

            if (extension is null && enabled)
            {
                var folder = StageBundledExtension(definition);
                extension = await profile.AddBrowserExtensionAsync(folder);
                SettingsService.SetValue(GetInstalledIdSettingKey(definition), extension.Id);
                SettingsService.SetValue(
                    GetInstalledBundleVersionSettingKey(definition),
                    definition.BundledVersion);
            }

            if (extension is not null && extension.IsEnabled != enabled)
            {
                await extension.EnableAsync(enabled);
            }
        }
        finally
        {
            ExtensionOperationGate.Release();
        }
    }

    public static async Task MaintainExtensionsAsync(CoreWebView2Profile profile)
    {
        var darkReaderId = SettingsService.GetValue(RetiredDarkReaderInstalledIdKey);
        if (!string.IsNullOrWhiteSpace(darkReaderId))
        {
            var installed = await profile.GetBrowserExtensionsAsync();
            var darkReader = installed.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, darkReaderId, StringComparison.Ordinal));

            if (darkReader is not null)
            {
                await darkReader.RemoveAsync();
            }
        }

        SettingsService.RemoveValue(RetiredDarkReaderInstalledIdKey);
        SettingsService.RemoveValue("extensions.darkMode.enabled");

        foreach (var definition in Extensions)
        {
            if (!bool.TryParse(SettingsService.GetValue(definition.SettingKey), out var enabled) ||
                !enabled)
            {
                continue;
            }

            try
            {
                await SetEnabledAsync(profile, definition, enabled: true);
            }
            catch
            {
                // Keep browser startup available. A later user toggle will surface the error.
            }
        }
    }

    private static string StageBundledExtension(BrowserExtensionDefinition definition)
    {
        var sourceFolder = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Extensions",
            definition.BundledFolderName!);
        var sourceManifest = Path.Combine(sourceFolder, "manifest.json");
        if (!File.Exists(sourceManifest))
        {
            throw new FileNotFoundException(
                $"{definition.DisplayName} is missing from the application package.",
                sourceManifest);
        }

        var stagedFolder = Path.Combine(
            Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "BrowserExtensions",
            definition.BundledFolderName!,
            definition.BundledVersion);
        var stagedManifest = Path.Combine(stagedFolder, "manifest.json");
        var stagedReadyMarker = $"{stagedFolder}.ready";
        if (File.Exists(stagedManifest) && File.Exists(stagedReadyMarker))
        {
            return stagedFolder;
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(
                     sourceFolder,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceFolder, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(stagedFolder, relativeDirectory));
        }

        Directory.CreateDirectory(stagedFolder);
        foreach (var sourceFile in Directory.EnumerateFiles(
                     sourceFolder,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourceFolder, sourceFile);
            var destinationFile = Path.Combine(stagedFolder, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        File.WriteAllText(stagedReadyMarker, definition.BundledVersion);
        return stagedFolder;
    }

    private static string GetInstalledIdSettingKey(BrowserExtensionDefinition definition) =>
        $"extensions.{definition.Id}.installedId";

    private static string GetInstalledBundleVersionSettingKey(
        BrowserExtensionDefinition definition) =>
        $"extensions.{definition.Id}.installedBundleVersion";
}
