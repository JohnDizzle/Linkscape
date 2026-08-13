using LinkScape.Browser;
using LinkScape.Models;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ShellJumpList = Windows.UI.StartScreen.JumpList;
using ShellJumpListItem = Windows.UI.StartScreen.JumpListItem;
using ShellJumpListSystemGroupKind = Windows.UI.StartScreen.JumpListSystemGroupKind;

namespace LinkScape.Services.Application;

public static class AppJumpListService
{
    private const int MaximumInstalledApps = 8;
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";
    private const string IconFolderName = "JumpListIcons";
    private static readonly Uri AppLogo = new("ms-appx:///Assets/Square44x44Logo.png");
    private static readonly HttpClient IconClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    public static async Task<bool> RefreshAsync(bool reportUnavailable = false)
    {
        if (!ShellJumpList.IsSupported())
        {
            if (reportUnavailable)
            {
                BrowserNoticeService.Show(
                    "Taskbar jump lists require the packaged LinkScape app. Launch the 'LinkScape Package' profile instead of dotnet run.",
                    "info");
            }

            return false;
        }

        try
        {
            await RefreshLock.WaitAsync();
            var jumpList = await ShellJumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = ShellJumpListSystemGroupKind.None;
            jumpList.Items.Clear();

            foreach (var collection in TabCollectionService.GetCollections()
                         .Where(collection => TabCollectionService.GetItems(collection.Id).Count > 0))
            {
                AddCollectionItem(jumpList, collection);
            }

            var provider = BrowserSearchProviders.GetByKey(
                SettingsService.GetValueOrDefault(
                    DefaultSearchProviderSettingKey,
                    BrowserSearchProviders.DefaultProviderKey));
            var providerLogo = await CacheIconAsync(
                $"search-{provider.Key}",
                BrowserSearchProviders.GetFaviconUrl(provider.Key));
            AddNavigationItem(
                jumpList,
                $"Search ({provider.DisplayName})",
                ActivationRoutingService.CreateNewWindowActivationArguments(
                    "link2scape://navigate/search"),
                providerLogo);

            foreach (var app in InstalledWebAppService.GetAll()
                         .OrderByDescending(static app => app.InstalledAt)
                         .Take(MaximumInstalledApps))
            {
                var item = ShellJumpListItem.CreateWithArguments(
                    $"link2scape://app/{Uri.EscapeDataString(app.Id)}",
                    string.IsNullOrWhiteSpace(app.ShortName) ? app.Name : app.ShortName);
                item.GroupName = "Apps";
                item.Description = $"Open {app.Name}";
                item.Logo = await GetAppLogoAsync(app);
                jumpList.Items.Add(item);
            }

            await jumpList.SaveAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not update the LinkScape jump list: {ex}");

            if (reportUnavailable)
            {
                BrowserNoticeService.Show($"Could not update the taskbar jump list: {ex.Message}");
            }

            return false;
        }
        finally
        {
            if (RefreshLock.CurrentCount == 0)
            {
                RefreshLock.Release();
            }
        }
    }

    private static void AddNavigationItem(
        ShellJumpList jumpList,
        string label,
        string arguments,
        Uri? logo = null)
    {
        var item = ShellJumpListItem.CreateWithArguments(arguments, label);
        item.GroupName = "Navigate";
        item.Description = $"Open LinkScape {label}";
        item.Logo = logo ?? AppLogo;
        jumpList.Items.Add(item);
    }

    private static void AddCollectionItem(ShellJumpList jumpList, TabCollection collection)
    {
        var item = ShellJumpListItem.CreateWithArguments(
            ActivationRoutingService.CreateNewWindowActivationArguments(
                $"link2scape://collection/{Uri.EscapeDataString(collection.Id)}"),
            collection.Name);
        item.GroupName = "Collections";
        item.Description = $"Set {collection.Name} as the startup collection and open it";
        item.Logo = AppLogo;
        jumpList.Items.Add(item);
    }

    private static async Task<Uri> GetAppLogoAsync(InstalledWebApp app)
    {
        if (string.IsNullOrWhiteSpace(app.IconUrl))
        {
            return AppLogo;
        }

        return await CacheIconAsync($"app-{app.Id}", app.IconUrl) ?? AppLogo;
    }

    private static async Task<Uri?> CacheIconAsync(string cacheKey, string? rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var iconUrl) ||
            (iconUrl.Scheme != Uri.UriSchemeHttp && iconUrl.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            var iconFolder = Path.Combine(localFolder, IconFolderName);
            var existingFile = SupportedIconExtensions
                .Select(extension => $"{cacheKey}{extension}")
                .FirstOrDefault(fileName => File.Exists(Path.Combine(iconFolder, fileName)));

            if (existingFile is not null)
            {
                return CreateAppDataIconUri(existingFile);
            }

            using var response = await IconClient.GetAsync(iconUrl);
            response.EnsureSuccessStatusCode();
            var extension = GetIconExtension(
                iconUrl.AbsolutePath,
                response.Content.Headers.ContentType?.MediaType);
            if (extension is null)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024)
            {
                return null;
            }

            var fileName = $"{cacheKey}{extension}";
            Directory.CreateDirectory(iconFolder);
            await File.WriteAllBytesAsync(Path.Combine(iconFolder, fileName), bytes);
            return CreateAppDataIconUri(fileName);
        }
        catch
        {
            return null;
        }
    }

    private static readonly string[] SupportedIconExtensions = [".png", ".jpg", ".jpeg", ".ico", ".gif", ".bmp"];

    private static Uri CreateAppDataIconUri(string fileName) =>
        new($"ms-appdata:///local/{IconFolderName}/{Uri.EscapeDataString(fileName)}");

    private static string? GetIconExtension(string path, string? mediaType)
    {
        var contentTypeExtension = mediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/svg+xml" or "image/webp" => null,
            _ => string.Empty
        };

        if (contentTypeExtension is null)
        {
            return null;
        }

        if (contentTypeExtension.Length > 0)
        {
            return contentTypeExtension;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return SupportedIconExtensions.Contains(extension) ? extension : null;
    }
}
