using LinkScape.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace LinkScape.Services;

/// <summary>
/// Opens LinkScape-managed installed web apps in their own WinUI 3 window.
/// The app WebView2 uses the same LinkScape WebView2 user-data folder so
/// cookies, service workers, local storage, IndexedDB, and sign-in state are shared.
/// </summary>
public static class WebAppWindowService
{
    private static readonly Dictionary<string, Window> OpenWindows =
        new(StringComparer.Ordinal);

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

        if (OpenWindows.TryGetValue(app.Id, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        _ = OpenCoreAsync(app);
    }

    private static async Task OpenCoreAsync(InstalledWebApp app)
    {
        if (!Uri.TryCreate(app.StartUrl, UriKind.Absolute, out var startUri) ||
            startUri.Scheme is not ("http" or "https"))
        {
            BrowserNoticeService.Show($"Could not open {app.Name}: the saved start URL is invalid.");
            return;
        }

        try
        {
            var window = new Window
            {
                Title = app.Name
            };

            var webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var root = new Grid();
            root.Children.Add(webView);
            window.Content = root;

            OpenWindows[app.Id] = window;

            window.Closed += (_, _) =>
            {
                OpenWindows.Remove(app.Id);

                try
                {
                    webView.Close();
                }
                catch
                {
                }
            };

            // Show the native window first so WebView2 has a live visual tree/XamlRoot.
            window.Activate();

            try
            {
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
            }
            catch
            {
                // Window sizing is best effort and should not block app launch.
            }

            await webView.EnsureCoreWebView2Async(await BrowserEnvironment.Value);

            var core = webView.CoreWebView2;
            if (core is null)
            {
                throw new InvalidOperationException("WebView2 could not be initialized for the installed app window.");
            }

            core.Settings.IsStatusBarEnabled = false;

            // Keep target=_blank/window.open links inside the app when they remain in scope.
            // Links outside the installed app's scope are handed back to the normal browser shell later.
            core.NewWindowRequested += (_, args) =>
            {
                if (IsWithinScope(args.Uri, app.Scope))
                {
                    args.Handled = true;
                    core.Navigate(args.Uri);
                }
            };

            core.Navigate(startUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            OpenWindows.Remove(app.Id);
            BrowserNoticeService.Show($"Could not open {app.Name}: {ex.Message}");
        }
    }

    private static bool IsWithinScope(string? rawUrl, string scope)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var target) ||
            !Uri.TryCreate(scope, UriKind.Absolute, out var scopeUri))
        {
            return false;
        }

        if (!string.Equals(target.Scheme, scopeUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Host, scopeUri.Host, StringComparison.OrdinalIgnoreCase) ||
            target.Port != scopeUri.Port)
        {
            return false;
        }

        return target.AbsolutePath.StartsWith(
            scopeUri.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
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
