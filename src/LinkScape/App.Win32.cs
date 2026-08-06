using LinkScape.Browser.Messages;
using LinkScape.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class MainWindowActivation
{
    private const string WindowPositionXSettingKey = "window.position.x";
    private const string WindowPositionYSettingKey = "window.position.y";
    private const string WindowWidthSettingKey = "window.size.width";
    private const string WindowHeightSettingKey = "window.size.height";
    private const string WindowMaximizedSettingKey = "window.state.maximized";
    private const int MinimumWindowX = 0;
    private const int MinimumWindowY = 0;
    private const int MinimumWindowWidth = 800;
    private const int MinimumWindowHeight = 400;
    private const int DefaultWindowWidth = 1200;
    private const int DefaultWindowHeight = 800;
    private const int MinimumRestoredWidth = 800;
    private const int MinimumRestoredHeight = 400;
    private const int DefaultRestoredWidth = 1200;
    private const int DefaultRestoredHeight = 800;
    private static readonly object SyncRoot = new();
    private static Window? _window;
    private static AppWindow? _appWindow;
    private static nint _hwnd;
    private static bool _isFullScreenPresentationActive;
    private static bool _restoreMaximizedAfterFullScreen;
    private static bool _messengerRegistered;
    public static nint Hwnd
    {
        get
        {
            lock (SyncRoot)
            {
                return _hwnd;
            }
        }
    }
    private static IMessenger Messenger => LinkScapeServiceProvider.GetRequiredService<IMessenger>();

    internal static bool IsFullScreenPresentationActive
    {
        get
        {
            lock (SyncRoot)
            {
                return _isFullScreenPresentationActive;
            }
        }
    }

    internal static void Register(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        lock (SyncRoot)
        {
            _window = window;
            _appWindow = appWindow;
            _hwnd = hwnd;

            if (!_messengerRegistered)
            {
                Messenger.Register<WebViewFullScreenPresentationRequestMessage>(
                    typeof(MainWindowActivation),
                    static (_, message) => SetWebViewFullScreenPresentation(message.IsFullScreen));
                _messengerRegistered = true;
            }
        }
    }

    internal static void RestoreWindowPlacement()
    {
        AppWindow? appWindow;

        lock (SyncRoot)
        {
            appWindow = _appWindow;
        }

        if (appWindow is null)
        {
            return;
        }

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

    internal static void SaveWindowPlacement()
    {
        AppWindow? appWindow;

        lock (SyncRoot)
        {
            appWindow = _appWindow;
        }

        if (appWindow is null)
        {
            return;
        }

        try
        {
            if (IsFullScreenPresentationActive)
            {
                return;
            }

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

    internal static void SetWebViewFullScreenPresentation(bool isFullScreen)
    {
        Window? window;

        lock (SyncRoot)
        {
            window = _window;
        }

        if (window is null)
        {
            return;
        }

        if (window.DispatcherQueue.HasThreadAccess)
        {
            ApplyWebViewFullScreenPresentation(isFullScreen);
            return;
        }

        _ = window.DispatcherQueue.TryEnqueue(() => ApplyWebViewFullScreenPresentation(isFullScreen));
    }

    private static void ApplyWebViewFullScreenPresentation(bool isFullScreen)
    {
        AppWindow? appWindow;

        lock (SyncRoot)
        {
            if (_isFullScreenPresentationActive == isFullScreen)
            {
                return;
            }

            appWindow = _appWindow;

            if (appWindow is null)
            {
                return;
            }

            _isFullScreenPresentationActive = isFullScreen;
        }

        try
        {
            if (isFullScreen)
            {
                _restoreMaximizedAfterFullScreen = appWindow.Presenter is OverlappedPresenter presenter &&
                    presenter.State == OverlappedPresenterState.Maximized;
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

                if (_restoreMaximizedAfterFullScreen &&
                    appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }

                _restoreMaximizedAfterFullScreen = false;
            }
        }
        catch
        {
        }

        Messenger.Send(new WebViewFullScreenPresentationChangedMessage(isFullScreen));
    }

    internal static Microsoft.UI.Xaml.XamlRoot? GetXamlRoot()
    {
        lock (SyncRoot)
        {
            return _window?.Content?.XamlRoot;
        }
    }

    internal static bool TryEnqueue(Action callback)
    {
        Window? window;

        lock (SyncRoot)
        {
            window = _window;
        }

        if (window is null)
        {
            return false;
        }

        if (window.DispatcherQueue.HasThreadAccess)
        {
            callback();
            return true;
        }

        return window.DispatcherQueue.TryEnqueue(() => callback());
    }

    private static int? ReadIntSetting(string key)
    {
        return int.TryParse(
            SettingsService.GetValue(key),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool IsValidWindowPosition(AppWindow appWindow, int x, int y, int width, int height)
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
}
