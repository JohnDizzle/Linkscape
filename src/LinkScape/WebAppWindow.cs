using LinkScape.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace LinkScape;

/// <summary>
/// Compact top-level host for a LinkScape-installed web app.
/// The window deliberately omits normal LinkScape browser chrome.
/// </summary>
internal sealed class WebAppWindow : Window
{
    private const int InitialWidth = 1180;
    private const int InitialHeight = 760;
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
            if (IsWithinScope(args.Uri, _app.Scope))
            {
                args.Handled = true;
                core.Navigate(args.Uri);
            }
        };

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
        var root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ChromeHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var chrome = new Grid
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

        var closeButton = new Button
        {
            Content = "\uE8BB",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            Width = 46,
            Height = ChromeHeight,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        closeButton.Click += (_, _) => Close();

        chrome.Children.Add(title);
        Grid.SetColumn(title, 0);
        chrome.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);

        root.Children.Add(chrome);
        Grid.SetRow(chrome, 0);
        root.Children.Add(_webView);
        Grid.SetRow(_webView, 1);

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

            AppWindow.Resize(new SizeInt32(InitialWidth, InitialHeight));
        }
        catch
        {
            // The window remains usable even if presenter customization is unavailable.
        }
    }

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
