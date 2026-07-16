using LinkScape.Browser;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
LinkScape.ActivationRoutingService.Initialize();
const string WindowPositionXSettingKey = "window.position.x";
const string WindowPositionYSettingKey = "window.position.y";
const string WindowWidthSettingKey = "window.size.width";
const string WindowHeightSettingKey = "window.size.height";
const string WindowMaximizedSettingKey = "window.state.maximized";
const int DefaultWindowWidth = 1200;
const int DefaultWindowHeight = 800;

ReactorApp.Run<App>("LinkScape",
    configure: host =>
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(host.Window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
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

    try
    {
        if (width > 0 && height > 0)
        {
            if (x is not null && y is not null)
            {
                appWindow.MoveAndResize(
                    new RectInt32(
                        x.Value,
                        y.Value,
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

        SettingsService.SetValue(WindowPositionXSettingKey, position.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SettingsService.SetValue(WindowPositionYSettingKey, position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SettingsService.SetValue(WindowWidthSettingKey, size.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SettingsService.SetValue(WindowHeightSettingKey, size.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

class App : Component
{
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";
    private const string BackdropGradientPresetDefault = "Default";
    private bool _settingsListenerRegistered;

    public override Element Render()
    {
        var (backdropGradientPreset, setBackdropGradientPreset) = UseState(
            NormalizeBackdropGradientPreset(
                SettingsService.GetValueOrDefault(
                    BackdropGradientPresetSettingKey,
                    BackdropGradientPresetDefault)));

        RegisterSettingsListener(setBackdropGradientPreset);

        return FlexColumn(
            TitleBar("LinkScape Browser").Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png"),   
            Component<LinkScape.TabViewPage>()
                .Flex(grow: 1, basis: 0)
        )
        .Background(CreateBackdropBrush(backdropGradientPreset))
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

            setBackdropGradientPreset(NormalizeBackdropGradientPreset(value));
        }
    }

    private static string NormalizeBackdropGradientPreset(string? preset)
    {
        return preset switch
        {
            "Aurora" => "Aurora",
            "Sunset" => "Sunset",
            "Ocean" => "Ocean",
            "Graphite" => "Graphite",
            "Forest" => "Forest",
            "HighContrast" => "HighContrast",
            "None" => BackdropGradientPresetDefault,
            _ => BackdropGradientPresetDefault
        };
    }

    private static Brush CreateBackdropBrush(string preset)
    {
        return NormalizeBackdropGradientPreset(preset) switch
        {

            "Aurora" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x75, 0x00, 0xC8, 0x8A),
                Microsoft.UI.ColorHelper.FromArgb(0x68, 0x00, 0x99, 0xFF),
                Microsoft.UI.ColorHelper.FromArgb(0x60, 0xA0, 0x5B, 0xFF)),


            "Sunset" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x75, 0xFF, 0x7A, 0x3D),
                Microsoft.UI.ColorHelper.FromArgb(0x68, 0xFF, 0xB3, 0x3B),
                Microsoft.UI.ColorHelper.FromArgb(0x60, 0xC7, 0x5A, 0xE6)),


            "Ocean" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x75, 0x00, 0x5E, 0xB8),
                Microsoft.UI.ColorHelper.FromArgb(0x68, 0x14, 0x4E, 0xC8),
                Microsoft.UI.ColorHelper.FromArgb(0x60, 0x3B, 0x78, 0xE8)),


            "Forest" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x75, 0x1F, 0x8B, 0x4C),
                Microsoft.UI.ColorHelper.FromArgb(0x68, 0x19, 0xA9, 0x74),
                Microsoft.UI.ColorHelper.FromArgb(0x60, 0x6B, 0xC4, 0x43)),

            "Graphite" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x75, 0x2A, 0x31, 0x3A),
                Microsoft.UI.ColorHelper.FromArgb(0x68, 0x44, 0x4E, 0x5C),
                Microsoft.UI.ColorHelper.FromArgb(0x60, 0x5A, 0x68, 0x79)),

            "HighContrast" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xA0, 0x00, 0x00, 0x00),
                Microsoft.UI.ColorHelper.FromArgb(0x90, 0x0A, 0x0A, 0x0A),
                Microsoft.UI.ColorHelper.FromArgb(0x80, 0x18, 0x18, 0x18)),

            _ => BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush
        };
    }

    private static Brush CreateGradientBrush(
        Windows.UI.Color start,
        Windows.UI.Color middle,
        Windows.UI.Color end)
    {
        return new LinearGradientBrush
        {
            // Top -> Bottom
            StartPoint = new Windows.Foundation.Point(0.5, 0.0),
            EndPoint = new Windows.Foundation.Point(0.5, 1.0),

            GradientStops = new GradientStopCollection
            {
                new() { Color = start,  Offset = 0.0 },
                new() { Color = middle, Offset = 0.5 },
                new() { Color = end,    Offset = 1.0 }
            }

        };
    }
}
