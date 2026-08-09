using LinkScape.Browser;
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
    private const int PreferredWidth = 560;
    private const int MinWidth = 420;
    private const int MaxWidth = 680;
    private const int MinHeight = 560;
    private const double TargetWorkAreaHeightRatio = 0.72;
    private const double MaxWorkAreaHeightRatio = 0.86;
    private const double ChromeHeight = 38;
    private const int CompactIslandHeight = 42;
    private const int CompactIslandGap = 8;
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";

    private readonly InstalledWebApp _app;
    private int _stackIndex;
    private readonly Microsoft.UI.Xaml.Controls.WebView2 _webView;
    private bool _isClosed;
    private bool _isCompact;
    private bool _hasShownFirstPage;
    private Microsoft.UI.Xaml.Controls.Button? _backButton;
    private Microsoft.UI.Xaml.Controls.Grid? _contentHost;
    private Microsoft.UI.Xaml.Controls.Grid? _startupSplash;

    internal event Action<WebAppWindow>? RestoreRequested;

    internal WebAppWindow(InstalledWebApp app, int stackIndex = 0)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _stackIndex = Math.Max(0, stackIndex);
        Title = app.Name;

        _webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = Microsoft.UI.Colors.Transparent,
            Opacity = 0
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
        core.NavigationCompleted += (_, _) =>
        {
            _webView.DispatcherQueue.TryEnqueue(HideStartupSplash);
        };

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
        core.HistoryChanged += (_, _) =>
        {
            _webView.DispatcherQueue.TryEnqueue(() =>
            {
                _backButton!.IsEnabled = core.CanGoBack;
            }); 

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

        var title = new TextBlock
        {
            Text = _app.Name,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(108, 0, 72, 0),
            FontSize = 14,
            Opacity = 0.82,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };

        var icon = new Microsoft.UI.Xaml.Controls.Image
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Source = _app.IconUrl is not null
                ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_app.IconUrl))
                : null
        };

        var reloadButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = BrowserConstants.GlyphRefresh,
                FontSize = 14,
                FontFamily = BrowserConstants.IconFontFamily,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Width = 36,
            Height = 28,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        reloadButton.Click += (_, _) =>
        {
            try
            {
                _webView.Reload();
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not reload the web view: {ex.Message}");
            }   
        };
        
        _backButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = BrowserConstants.GlyphBack,
                FontSize = 14,
                FontFamily = BrowserConstants.IconFontFamily,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Width = 36,
            Height = 28,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsEnabled = false
        };
        _backButton.Click += (_, _) =>
        {
            try
            {
                _webView.GoBack();
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not navigate back: {ex.Message}");
            }
        };

        var controls = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 4,
            Padding = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        controls.Children.Add(icon);
        controls.Children.Add(_backButton);
        controls.Children.Add(reloadButton);
        controls.Tapped += (_, _) =>
        {
            if (_isCompact)
            {
                RestoreRequested?.Invoke(this);
            }
        };

        chrome.Children.Add(title);
        chrome.Children.Add(controls);
        
        root.Children.Add(chrome);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(chrome, 0);
        _contentHost = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = CreateStartupBackdropBrush()
        };
        _contentHost.Children.Add(_webView);
        _startupSplash = BuildStartupSplash();
        _contentHost.Children.Add(_startupSplash);
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(_startupSplash, 1);

        root.Children.Add(_contentHost);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(_contentHost, 1);

        // The native title bar is removed below. SetTitleBar keeps this strip as the
        // draggable title region while the close button remains interactive.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(chrome);

        return root;
    }

    private Microsoft.UI.Xaml.Controls.Grid BuildStartupSplash()
    {
        var splash = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = CreateStartupBackdropBrush(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var title = new TextBlock
        {
            Text = _app.ShortName ?? _app.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.86
        };

        var message = new TextBlock
        {
            Text = "Opening app workspace",
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.68
        };

        var cardContent = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildLogoWithBadge(),
                title,
                message,
                BuildStartupProgress()
            }
        };

        var card = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 388,
            MaxWidth = 420,
            Padding = new Thickness(28),
            CornerRadius = new CornerRadius(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = cardContent
        };
        splash.Children.Add(card);

        return splash;
    }

    private Microsoft.UI.Xaml.Controls.Grid BuildLogoWithBadge()
    {
        var logoBadge = new Microsoft.UI.Xaml.Controls.Grid
        {
            Width = 186,
            Height = 186,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var halo = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 182,
            Height = 182,
            CornerRadius = new CornerRadius(91),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1.2)
        };
        logoBadge.Children.Add(halo);

        var favicon = new Microsoft.UI.Xaml.Controls.Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(GetStartupLogoUri())
        };
        ConfigureFaviconSpin(favicon);

        var iconFrame = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 132,
            Height = 132,
            CornerRadius = new CornerRadius(34),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10),
            Background = BrowserMaterialTheme.GlassStrongFillBrush,
            BorderBrush = BrowserMaterialTheme.SelectedStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = favicon
        };
        logoBadge.Children.Add(iconFrame);

        var badgeStrokeTransform = new RotateTransform
        {
            CenterX = 0.5,
            CenterY = 0.5
        };
        var badge = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 22, 24),
            Background = BrowserMaterialTheme.GlassStrongFillBrush,
            BorderBrush = BrowserMaterialTheme.CreateActivityStrokeBrush(badgeStrokeTransform),
            BorderThickness = new Thickness(1.5),
            Child = new TextBlock
            {
                Text = BrowserConstants.GlyphGlobe,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 15,
                Foreground = BrowserMaterialTheme.BadgeForegroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        ConfigureBadgeFire(badge, badgeStrokeTransform);
        logoBadge.Children.Add(badge);

        return logoBadge;
    }

    private Microsoft.UI.Xaml.Controls.Border BuildStartupProgress()
    {
        var progress = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 92,
            Height = 5,
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = BrowserMaterialTheme.SelectedStrokeBrush
        };
        ConfigureStartupProgress(progress);

        return new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 252,
            Height = 11,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = progress
        };
    }

    private void ConfigureStartupProgress(Microsoft.UI.Xaml.Controls.Border progress)
    {
        var transform = new TranslateTransform { X = -74 };
        progress.RenderTransform = transform;

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = -74,
            To = 228,
            Duration = new Duration(TimeSpan.FromSeconds(1.35)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "X");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ConfigureFaviconSpin(Microsoft.UI.Xaml.Controls.Image favicon)
    {
        var projection = new PlaneProjection
        {
            CenterOfRotationX = 0.5,
            CenterOfRotationY = 0.5
        };
        favicon.Projection = projection;

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(4.8)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, projection);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "RotationY");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ConfigureBadgeFire(
        Microsoft.UI.Xaml.Controls.Border badge,
        RotateTransform badgeStrokeTransform)
    {
        var strokeAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(2.8)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(strokeAnimation, badgeStrokeTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(strokeAnimation, "Angle");

        var opacityAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0.82,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(0.9)),
            AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnimation, badge);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(strokeAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Begin();
    }

    private void HideStartupSplash()
    {
        if (_hasShownFirstPage)
        {
            return;
        }

        _hasShownFirstPage = true;
        _webView.Opacity = 1;

        if (_startupSplash is null)
        {
            return;
        }

        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, _startupSplash);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) =>
        {
            if (_startupSplash is not null)
            {
                _startupSplash.Visibility = Visibility.Collapsed;
            }
        };
        storyboard.Begin();
    }

    private Uri GetStartupLogoUri()
    {
        return Uri.TryCreate(_app.IconUrl, UriKind.Absolute, out var iconUri)
            ? iconUri
            : new Uri("ms-appx:///Assets/StoreLogo.png");
    }

    private Brush CreateStartupBackdropBrush()
    {
        var preset = AppBackdropBrushes.NormalizePreset(
            SettingsService.GetValueOrDefault(
                BackdropGradientPresetSettingKey,
                AppBackdropBrushes.DefaultPreset));

        return AppBackdropBrushes.CreateBrush(preset);
    }

    private void ConfigureNativeWindow()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
            }

            var displayArea =
                Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var workArea = displayArea?.WorkArea;
            var width = GetInitialWindowWidth(workArea);
            var height = GetInitialWindowHeight(workArea);

            AppWindow.Resize(
                new Windows.Graphics.SizeInt32(
                    width,
                    height));

            ApplyIslandState(_stackIndex, isCompact: false);
        }
        catch(Exception ex)
        {
            BrowserNoticeService.Show($"Could not position the app window: {ex.Message}");
        }
        // Keep the window usable even if positioning fails.
    }
    internal void ApplyIslandState(int stackIndex, bool isCompact)
    {
        _stackIndex = Math.Max(0, stackIndex);
        _isCompact = isCompact;

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

            var width = isCompact
                ? GetInitialWindowWidth(workArea)
                : GetInitialWindowWidth(workArea);
            var height = isCompact
                ? CompactIslandHeight
                : GetInitialWindowHeight(workArea);
            const int margin = 12;

            var targetX =
                workArea.X +
                workArea.Width -
                width -
                margin;

            var targetY = isCompact
                ? workArea.Y + margin + (_stackIndex * (CompactIslandHeight + CompactIslandGap))
                : workArea.Y + workArea.Height - height - margin;

            var x = Math.Clamp(
                targetX,
                workArea.X + margin,
                Math.Max(workArea.X + margin, workArea.X + workArea.Width - width - margin));
            var y = Math.Clamp(
                targetY,
                workArea.Y + margin,
                Math.Max(workArea.Y + margin, workArea.Y + workArea.Height - height - margin));

            AppWindow.MoveAndResize(
                new Windows.Graphics.RectInt32(
                    x,
                    y,
                    width,
                    height));

            if (_contentHost is not null)
            {
                _contentHost.Visibility = isCompact
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

        }
        catch (Exception ex)
        {
            BrowserNoticeService.Show($"Could not position the app window: {ex.Message}");
        }
    }

    private static int GetInitialWindowWidth(Windows.Graphics.RectInt32? workArea)
    {
        if (workArea is null)
        {
            return PreferredWidth;
        }

        var maxByWorkArea = Math.Max(MinWidth, workArea.Value.Width - 24);
        return Math.Clamp(PreferredWidth, MinWidth, Math.Min(MaxWidth, maxByWorkArea));
    }

    private static int GetInitialWindowHeight(Windows.Graphics.RectInt32? workArea)
    {
        if (workArea is null)
        {
            return MinHeight;
        }

        var targetHeight = (int)Math.Round(workArea.Value.Height * TargetWorkAreaHeightRatio);
        var maxHeight = Math.Max(MinHeight, (int)Math.Round(workArea.Value.Height * MaxWorkAreaHeightRatio));
        var guardedMaxHeight = Math.Min(maxHeight, Math.Max(MinHeight, workArea.Value.Height - 24));

        return Math.Clamp(targetHeight, MinHeight, guardedMaxHeight);
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
