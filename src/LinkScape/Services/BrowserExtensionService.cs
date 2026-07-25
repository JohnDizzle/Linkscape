using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Threading.Tasks;

namespace LinkScape.Services;

internal sealed record BrowserExtensionDefinition(
    string Id,
    string DisplayName,
    string Description,
    string SettingKey,
    string? BundledFolderName,
    string License,
    string ProjectUrl)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(BundledFolderName);
}

internal static class BrowserExtensionService
{
    public static readonly IReadOnlyList<BrowserExtensionDefinition> Extensions =
    [
        new("ublock-origin-lite", "uBlock Origin Lite",
            "Blocks ads and trackers with Manifest V3 rules.",
            "extensions.ublockOriginLite.enabled", "uBlockOriginLite", "GPL-3.0",
            "https://github.com/uBlockOrigin/uBOL-home"),
        new("dark-mode", "Dark Mode",
            "A comfortable theme for web pages. Coming next.",
            "extensions.darkMode.enabled", null, "Not selected", string.Empty),
        new("translate", "Translate",
            "Translate pages without leaving LinkScape. Coming next.",
            "extensions.translate.enabled", null, "Not selected", string.Empty)
    ];

    public static async Task SetEnabledAsync(
        CoreWebView2Profile profile,
        BrowserExtensionDefinition definition,
        bool enabled)
    {
        if (!definition.IsAvailable)
        {
            throw new InvalidOperationException($"{definition.DisplayName} is not bundled yet.");
        }

        var installed = await profile.GetBrowserExtensionsAsync();
        var extensionId = SettingsService.GetValue(GetInstalledIdSettingKey(definition));
        var extension = installed.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, extensionId, StringComparison.Ordinal));

        if (extension is null && enabled)
        {
            var folder = Path.Combine(
                AppContext.BaseDirectory, "Assets", "Extensions", definition.BundledFolderName!);

            if (!File.Exists(Path.Combine(folder, "manifest.json")))
            {
                throw new FileNotFoundException(
                    $"{definition.DisplayName} is missing from the application package.",
                    Path.Combine(folder, "manifest.json"));
            }

            extension = await profile.AddBrowserExtensionAsync(folder);
            SettingsService.SetValue(GetInstalledIdSettingKey(definition), extension.Id);
        }

        if (extension is not null && extension.IsEnabled != enabled)
        {
            await extension.EnableAsync(enabled);
        }
    }

    private static string GetInstalledIdSettingKey(BrowserExtensionDefinition definition) =>
        $"extensions.{definition.Id}.installedId";
}
