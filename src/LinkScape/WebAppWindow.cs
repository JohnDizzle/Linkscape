using LinkScape.Models;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace LinkScape;

/// <summary>
/// Compact top-level host for a LinkScape-installed web app.
/// The window deliberately omits normal LinkScape browser chrome.
/// </summary>
///

internal sealed class WebAppWindow : Window
{
    // TODO: Add a per-installed-app setting for "Use mobile layout".
    // When enabled, configure the app WebView2 with a mobile User-Agent
    // before navigation. Keep the normal WebView2 User-Agent as the default.
       
    ////[
       //// UseMobileLayout
       //// WindowX
       //// WindowY
       //// WindowWidth
       //// WindowHeight]
        
    // TODO: Persist each installed app window's last position and size
    // (X, Y, Width, Height), and restore them the next time the app opens.
    // Use the default 600x900 bottom-right placement only on first launch.
    private const int InitialWidth = 600;
    private const int InitialHeight = 960;
    private const double ChromeHeight = 38;

    private readonly InstalledWebApp _app;
    private readonly Microsoft.UI.Xaml.Controls.WebView2 _webView;
    private bool _isClosed;

    internal WebAppWindow(InstalledWebApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Title = app.Name;

        _webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Content = BuildSurface();
        ConfigureNativeWindow();
    }

    internal async Task InitializeAsync(CoreWebView2Environment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!Uri.TryCreate(_app.StartUrl, UriKind.Absolute, out var startUri) ||
            startUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("The installed app start URL is invalid.");
        }

        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 could not be initialized for the installed app window.");

        

        core.Settings.IsStatusBarEnabled = false;

        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;

            if (IsWithinScope(args.Uri, _app.Scope))
            {
                
                core.Navigate(args.Uri);
            }
            else
            {
                BrowserNoticeService.Show($"The app attempted to open a new window to {args.Uri}, which is outside the app's scope. This action was blocked.");
            }

           
        };

        await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
                window.addEventListener('DOMContentLoaded', function () {
                    document.documentElement.style.zoom = '85%';
                });
            ");

        core.Navigate(startUri.AbsoluteUri);
    }

    internal void DisposeWebView()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;

        try
        {
            _webView.Close();
        }
        catch
        {
        }
    }

    private UIElement BuildSurface()
    {
        var root = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ChromeHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var chrome = new Microsoft.UI.Xaml.Controls.Grid
        {
            Height = ChromeHeight,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 24))
        };
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = _app.Name,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0),
            FontSize = 12,
            Opacity = 0.82,
            IsHitTestVisible = false
        };

        var icon = new Microsoft.UI.Xaml.Controls.Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(8, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Source = _app.IconUrl is not null
                ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_app.IconUrl))
                : null
        };  

        var stackPanel = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        stackPanel.Children.Add(icon);
        stackPanel.Children.Add(title);

        chrome.Children.Add(stackPanel);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(stackPanel, 0);
        
        root.Children.Add(chrome);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(chrome, 0);
        root.Children.Add(_webView);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(_webView, 1);

        // The native title bar is removed below. SetTitleBar keeps this strip as the
        // draggable title region while the close button remains interactive.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(chrome);

        return root;
    }

    private void ConfigureNativeWindow()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            var width = InitialWidth;
            var height = InitialHeight;

            AppWindow.Resize(
                new Windows.Graphics.SizeInt32(
                    width,
                    height));

            MoveToBottomRight(
                width,
                height);
        }
        catch(Exception ex)
        {
            BrowserNoticeService.Show($"Could not position the app window: {ex.Message}");
        }
        // Keep the window usable even if positioning fails.
    }
    private void MoveToBottomRight(
    int width,
    int height)
    {
        try
        {
            var displayArea =
                Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

            if (displayArea is null)
            {
                return;
            }

            var workArea =
                displayArea.WorkArea;

            const int margin = 12;

            var x =
                workArea.X +
                workArea.Width -
                width -
                margin;

            var y =
                workArea.Y +
                workArea.Height -
                height -
                margin;

            AppWindow.MoveAndResize(
                new Windows.Graphics.RectInt32(
                    x,
                    y,
                    width,
                    height));
        }
        catch (Exception ex)
        {
            BrowserNoticeService.Show($"Could not position the app window: {ex.Message}");
        }
    }

    /*private void MoveToBottomRightWin32(
    int width,
    int height)
{
    var hwnd =
        WinRT.Interop.WindowNative.GetWindowHandle(this);

    if (hwnd == 0)
    {
        return;
    }

    var monitor =
        PInvoke.MonitorFromWindow(
            new HWND(hwnd),
            MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

    var info =
        new MONITORINFO
        {
            cbSize =
                (uint)System.Runtime.InteropServices.Marshal
                    .SizeOf<MONITORINFO>()
        };

    if (!PInvoke.GetMonitorInfo(
            monitor,
            ref info))
    {
        return;
    }

    const int margin = 12;

    var work =
        info.rcWork;

    var x =
        work.right -
        width -
        margin;

    var y =
        work.bottom -
        height -
        margin;

    PInvoke.SetWindowPos(
        new HWND(hwnd),
        HWND.Null,
        x,
        y,
        width,
        height,
        SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
        SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
}*/
    private static bool IsWithinScope(string? rawUrl, string scope)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var target) ||
            !Uri.TryCreate(scope, UriKind.Absolute, out var scopeUri))
        {
            return false;
        }

        return string.Equals(target.Scheme, scopeUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target.Host, scopeUri.Host, StringComparison.OrdinalIgnoreCase) &&
            target.Port == scopeUri.Port &&
            target.AbsolutePath.StartsWith(scopeUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }
}
