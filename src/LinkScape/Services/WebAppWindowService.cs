using LinkScape.Models;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Threading.Tasks;

namespace LinkScape.Services;

/// <summary>
/// Launches LinkScape-managed installed web apps in separate compact windows.
/// Window lifetime is owned by AppWindowRegistry rather than by this service.
/// </summary>
public static class WebAppWindowService
{
    private static readonly Lazy<Task<CoreWebView2Environment>> BrowserEnvironment =
        new(CreateBrowserEnvironmentAsync);
    private static WebAppWindow? _expandedWindow;

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
            if (existingWindow is WebAppWindow existingWebAppWindow)
            {
                ExpandWebAppWindow(existingWebAppWindow);
            }

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
            var stackIndex = AppWindowRegistry.Count(AppWindowKind.WebApp);
            window = new WebAppWindow(app, stackIndex);
            AppWindowRegistry.Register(key, window, AppWindowKind.WebApp);
            window.RestoreRequested += ExpandWebAppWindow;
            _expandedWindow = window;

            window.Closed += (_, _) =>
            {
                AppWindowRegistry.Unregister(key, window);
                window.DisposeWebView();
                if (ReferenceEquals(_expandedWindow, window))
                {
                    _expandedWindow = null;
                }

                ReflowWebAppWindows();
            };

            // Activate before WebView2 initialization so the window has a live XamlRoot/HWND.
            window.Activate();
      
            await window.InitializeAsync(await BrowserEnvironment.Value);

            await Task.Delay(500); // Give the window a moment to render before showing it.

        }
        catch (Exception ex)
        {
            if (window is not null)
            {
                AppWindowRegistry.Unregister(key, window);
                window.DisposeWebView();
                if (ReferenceEquals(_expandedWindow, window))
                {
                    _expandedWindow = null;
                }

                ReflowWebAppWindows();

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

    private static void ExpandWebAppWindow(WebAppWindow window)
    {
        _expandedWindow = window;
        ReflowWebAppWindows();
        window.Activate();
    }

    private static void ReflowWebAppWindows()
    {
        var windows = AppWindowRegistry.GetWindows<WebAppWindow>(AppWindowKind.WebApp);
        if (windows.Count == 0)
        {
            return;
        }

        _expandedWindow ??= windows[^1];

        var compactIndex = 0;
        foreach (var window in windows)
        {
            if (ReferenceEquals(window, _expandedWindow))
            {
                window.ApplyIslandState(0, isCompact: false);
                continue;
            }

            window.ApplyIslandState(compactIndex++, isCompact: true);
        }
    }

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
