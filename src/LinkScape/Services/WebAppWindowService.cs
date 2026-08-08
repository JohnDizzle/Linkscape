using LinkScape.Models;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace LinkScape.Services;

/// <summary>
/// Launches LinkScape-managed installed web apps in separate compact windows.
/// Window lifetime is owned by AppWindowRegistry rather than by this service.
/// </summary>
public static class WebAppWindowService
{
    private static readonly Lazy<Task<CoreWebView2Environment>> BrowserEnvironment =
        new(CreateBrowserEnvironmentAsync);

    public static bool TryOpenByManifestUrl(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return false;
        }

        var app = InstalledWebAppService
            .GetAll()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ManifestUrl,
                    manifestUrl,
                    StringComparison.OrdinalIgnoreCase));

        if (app is null)
        {
            return false;
        }

        Open(app);
        return true;
    }

    public static void Open(InstalledWebApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var key = GetWindowKey(app.Id);
        if (AppWindowRegistry.TryGet(key, out var existingWindow) && existingWindow is not null)
        {
            existingWindow.Activate();
            return;
        }

        _ = OpenCoreAsync(app, key);
    }

    public static bool TryOpenById(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        var app = InstalledWebAppService.Get(appId);
        if (app is null)
        {
            return false;
        }

        Open(app);
        return true;
    }

    private static async Task OpenCoreAsync(InstalledWebApp app, string key)
    {
        WebAppWindow? window = null;

        try
        {
            window = new WebAppWindow(app);
            AppWindowRegistry.Register(key, window);

            window.Closed += (_, _) =>
            {
                AppWindowRegistry.Unregister(key, window);
                window.DisposeWebView();
            };

            // Activate before WebView2 initialization so the window has a live XamlRoot/HWND.
            window.Activate();
            await window.InitializeAsync(await BrowserEnvironment.Value);
        }
        catch (Exception ex)
        {
            if (window is not null)
            {
                AppWindowRegistry.Unregister(key, window);
                window.DisposeWebView();

                try
                {
                    window.Close();
                }
                catch
                {
                }
            }

            BrowserNoticeService.Show($"Could not open {app.Name}: {ex.Message}");
        }
    }

    private static string GetWindowKey(string appId) => $"webapp:{appId}";

    private static Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(
            Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "WebView2");

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true
        };

        return CoreWebView2Environment.CreateWithOptionsAsync(
            string.Empty,
            userDataFolder,
            options).AsTask();
    }
}
