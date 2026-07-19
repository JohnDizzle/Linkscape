using LinkScape.Browser;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

using Windows.Win32.UI.WindowsAndMessaging;

var commandLineArgs = Environment.GetCommandLineArgs();

if (await LocalMcpServerService.TryRunAsync(commandLineArgs))
{
    return;
}

if (!await LinkScape.ActivationRoutingService.InitializeAsync())
{
    return;
}

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
const string WindowPositionXSettingKey = "window.position.x";
const string WindowPositionYSettingKey = "window.position.y";
const string WindowWidthSettingKey = "window.size.width";
const string WindowHeightSettingKey = "window.size.height";
const string WindowMaximizedSettingKey = "window.state.maximized";
const int MinimumWindowX = 0;
const int MinimumWindowY = 0;
const int MinimumWindowWidth = 800;
const int MinimumWindowHeight = 400;
const int DefaultWindowWidth = 1200;
const int DefaultWindowHeight = 800;

ReactorApp.Run<App>("LinkScape",
    configure: host =>
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(host.Window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        MainWindowActivation.Register(host.Window, appWindow);
        var restored = false;

        host.Window.Activated += (s, e) =>
        {
            if (restored || e.WindowActivationState == WindowActivationState.Deactivated)
            {  
                return;
            }

            restored = true;
            RestoreWindowPlacement(appWindow);
            
        };

        host.Window.Closed += (_, _) => SaveWindowPlacement(appWindow);
    });

static void RestoreWindowPlacement(AppWindow appWindow)
{
    var width = ReadIntSetting(WindowWidthSettingKey) ?? DefaultWindowWidth;
    var height = ReadIntSetting(WindowHeightSettingKey) ?? DefaultWindowHeight;
    var x = ReadIntSetting(WindowPositionXSettingKey);
    var y = ReadIntSetting(WindowPositionYSettingKey);

    width = width < MinimumWindowWidth ? DefaultWindowWidth : width;
    height = height < MinimumWindowHeight ? DefaultWindowHeight : height;
    var hasValidPosition = x is not null &&
        y is not null &&
        IsValidWindowPosition(appWindow, x.Value, y.Value, width, height);

    try
    {
        if (width >= MinimumWindowWidth && height >= MinimumWindowHeight)
        {
            if (hasValidPosition)
            {
                appWindow.MoveAndResize(
                    new RectInt32(
                        x!.Value,
                        y!.Value,
                        width,
                        height));
            }
            else
            {
                appWindow.Resize(
                    new SizeInt32(
                        width,
                        height));
            }
        }

        if (appWindow.Presenter is OverlappedPresenter presenter &&
            bool.TryParse(SettingsService.GetValue(WindowMaximizedSettingKey), out var isMaximized) &&
            isMaximized)
        {
            presenter.Maximize();
        }
    }
    catch
    {
    }
}

static void SaveWindowPlacement(AppWindow appWindow)
{
    try
    {
        var position = appWindow.Position;
        var size = appWindow.Size;
        var isMaximized = appWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Maximized;

        if (size.Width >= MinimumWindowWidth &&
            size.Height >= MinimumWindowHeight &&
            IsValidWindowPosition(appWindow, position.X, position.Y, size.Width, size.Height))
        {
            SettingsService.SetValue(WindowPositionXSettingKey, position.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.SetValue(WindowPositionYSettingKey, position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.SetValue(WindowWidthSettingKey, size.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.SetValue(WindowHeightSettingKey, size.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        SettingsService.SetValue(WindowMaximizedSettingKey, isMaximized ? "true" : "false");
    }
    catch
    {
    }
}

static int? ReadIntSetting(string key)
{
    return int.TryParse(
        SettingsService.GetValue(key),
        System.Globalization.NumberStyles.Integer,
        System.Globalization.CultureInfo.InvariantCulture,
        out var value)
        ? value
        : null;
}

static bool IsValidWindowPosition(AppWindow appWindow, int x, int y, int width, int height)
{
    if (x < MinimumWindowX || y < MinimumWindowY)
    {
        return false;
    }

    try
    {
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var visibleInset = 80;
        var right = x + width;
        var bottom = y + height;

        return right > workArea.X + visibleInset &&
            bottom > workArea.Y + visibleInset &&
            x < workArea.X + workArea.Width - visibleInset &&
            y < workArea.Y + workArea.Height - visibleInset;
    }
    catch
    {
        return true;
    }
}

internal static class MainWindowActivation
{
    private const int MinimumRestoredWidth = 800;
    private const int MinimumRestoredHeight = 400;
    private const int DefaultRestoredWidth = 1200;
    private const int DefaultRestoredHeight = 800;
    private static readonly object SyncRoot = new();
    private static Window? _window;
    private static AppWindow? _appWindow;
    private static nint _hwnd;

    internal static void Register(Window window, AppWindow appWindow)
    {
        lock (SyncRoot)
        {
            _window = window;
            _appWindow = appWindow;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
    }

    internal static void RestoreAndActivate()
    {
        Window? window;
        AppWindow? appWindow;
        nint hwnd;

        lock (SyncRoot)
        {
            window = _window;
            appWindow = _appWindow;
            hwnd = _hwnd;
        }

        if (window is null || appWindow is null || hwnd == 0)
        {
            return;
        }

        try
        {
            var windowHandle = new HWND(hwnd);

            if (appWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State != OverlappedPresenterState.Restored)
            {
                presenter.Restore();
            }

            if (PInvoke.IsIconic(windowHandle))
            {
                PInvoke.ShowWindow(windowHandle, SHOW_WINDOW_CMD.SW_RESTORE);
            }

            var size = appWindow.Size;

            if (size.Width < MinimumRestoredWidth || size.Height < MinimumRestoredHeight)
            {
                appWindow.Resize(new SizeInt32(DefaultRestoredWidth, DefaultRestoredHeight));
            }

            window.Activate();
            _ = PInvoke.SetForegroundWindow(windowHandle);
        }
        catch
        {
            // logerror("Failed to restore and activate main window", ex);
        }
    }

    internal static Microsoft.UI.Xaml.XamlRoot? GetXamlRoot()
    {
        lock (SyncRoot)
        {
            return _window?.Content?.XamlRoot;
        }
    }
}

class App : Component
{
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";
    private const int StartupSplashDurationMilliseconds = 1010;
    private static readonly object UnhandledExceptionSyncRoot = new();
    private static bool _unhandledExceptionHandlerRegistered;
    private bool _errorListenerRegistered;
    private bool _settingsListenerRegistered;
    private bool _startupSplashDismissScheduled;

    public override Element Render()
    {
        var backdropGradientPreset = UseState(
            LinkScape.AppBackdropBrushes.NormalizePreset(
                SettingsService.GetValueOrDefault(
                    BackdropGradientPresetSettingKey,
                    LinkScape.AppBackdropBrushes.DefaultPreset)));
        var fatalError = UseState<Exception?>(LinkScape.AppErrorStateService.CurrentError, threadSafe: true);
        var isShowingStartupSplash = UseState(true, threadSafe: true);

        RegisterSettingsListener(backdropGradientPreset.Set);
        RegisterErrorListener(fatalError.Set);
        RegisterUnhandledExceptionHandler();
        ScheduleStartupSplashDismissal(isShowingStartupSplash.Set);

        try
        {

            return fatalError.Value is not null
                ? BuildErrorSurface(backdropGradientPreset.Value, fatalError.Value)
                : isShowingStartupSplash.Value
                    ? LinkScape.AppLoadingSurface.Build()
                : BuildMainSurface(backdropGradientPreset.Value);
        }
        catch (Exception ex)
        {
            LinkScape.AppErrorStateService.SetError(ex);
            return BuildErrorSurface(backdropGradientPreset.Value, ex);
        }
    }

    private static void RegisterUnhandledExceptionHandler()
    {
        lock (UnhandledExceptionSyncRoot)
        {
            if (_unhandledExceptionHandlerRegistered)
            {
                return;
            }

            var application = Application.Current;

            if (application is null)
            {
                return;
            }

            application.UnhandledException += OnUnhandledException;
            _unhandledExceptionHandlerRegistered = true;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        LinkScape.AppErrorStateService.SetError(args.Exception);
        args.Handled = true;
    }

    private static void RestartApplication()
    {
        try
        {
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });

            Environment.Exit(0);
        }
        catch
        {
        }
    }

    private static void OpenDeveloperContact()
    {
        try
        {
            var subject = Uri.EscapeDataString("LinkScape error details");
            var body = Uri.EscapeDataString("Please paste the copied error details here.");
            var uri = new Uri($"mailto:dbamdin@fizzledbydizzlelive.onmicrosoft?subject={subject}&body={body}");
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
        }
    }

    private void RegisterErrorListener(Action<Exception?> setFatalError)
    {
        if (_errorListenerRegistered)
        {
            return;
        }

        _errorListenerRegistered = true;
        LinkScape.AppErrorStateService.ErrorChanged += OnErrorChanged;

        void OnErrorChanged()
        {
            setFatalError(LinkScape.AppErrorStateService.CurrentError);
        }
    }

    private static Element BuildMainSurface(string backdropGradientPreset)
    {
        return FlexColumn(
            TitleBar("LinkScape Browser").Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png"),
            Component<LinkScape.TabViewPage>()
                .Flex(grow: 1, basis: 0)
        )
        .Background(LinkScape.AppBackdropBrushes.CreateBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
    }

    private void ScheduleStartupSplashDismissal(Action<bool> setIsShowingStartupSplash)
    {
        if (_startupSplashDismissScheduled)
        {
            return;
        }

        _startupSplashDismissScheduled = true;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(StartupSplashDurationMilliseconds);
            setIsShowingStartupSplash(false);
        });
    }

    private static Element BuildErrorSurface(string backdropGradientPreset, Exception error)
    {
        return FlexColumn(
            TitleBar("LinkScape Browser").Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png"),
            Border(
                VStack(
                    12,
                    (TextBlock("LinkScape Browser hit an unexpected error") with
                    {
                        FontSize = 28,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    (TextBlock("The app shell was replaced with a safe page so you can recover without a raw stack trace.") with
                    {
                        FontSize = 14,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Opacity(0.82),
                    Border(
                        TextBlock(error.Message) with
                        {
                            FontSize = 13,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Padding(14)
                        .CornerRadius(12)
                        .Background(BrowserConstants.LayerFillDefaultBrush),
                    HStack(
                        10,
                        Button("Retry", LinkScape.AppErrorStateService.Clear)
                            .AutomationName("Retry app shell")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18),
                        Button("Restart app", RestartApplication)
                            .AutomationName("Restart application")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18),
                        Button("Copy details", () =>
                        {
                            try
                            {
                                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                                package.SetText(error.ToString());
                                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                            }
                            catch
                            {
                            }
                        })
                            .AutomationName("Copy error details")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18)
                    ),
                    (FlexRow(
                        (TextBlock("You may also send the details to ") with
                        {
                            FontSize = 12,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.72),
                        Button("Developer", OpenDeveloperContact)
                            .AutomationName("Developer contact")
                            .Padding(0)
                            .CornerRadius(14),
                        (TextBlock(" if you want direct help.") with
                        {
                            FontSize = 12,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.72)
                    ) with
                    {
                        ColumnGap = 4
                    })
                    .VAlign(VerticalAlignment.Center)
                )
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .MaxWidth(640)
                .Padding(28)
                .CornerRadius(24)
                .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xD8, 0x4B, 0x1F, 0x24)))
                .WithBorder(Theme.SurfaceStroke)
            )
            .Padding(32)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
        )
        .Background(LinkScape.AppBackdropBrushes.CreateBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
    }

    private void RegisterSettingsListener(Action<string> setBackdropGradientPreset)
    {
        if (_settingsListenerRegistered)
        {
            return;
        }

        _settingsListenerRegistered = true;
        SettingsService.SettingChanged += OnSettingChanged;

        void OnSettingChanged(string key, string? value)
        {
            if (!string.Equals(key, BackdropGradientPresetSettingKey, StringComparison.Ordinal))
            {
                return;
            }

            setBackdropGradientPreset(LinkScape.AppBackdropBrushes.NormalizePreset(value));
        }
    }
}
