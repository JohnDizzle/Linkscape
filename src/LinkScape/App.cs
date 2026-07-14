using Microsoft.UI;
using Microsoft.UI.Reactor;

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
ReactorApp.Run<App>("AI_Agent", width: 1200, height: 800);

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
            TitleBar("Linkscape")
                ,
            Component<AI_Agent.TabViewPage>()
                .Flex(grow: 1, basis: 0)
        )
        .Background(CreateBackdropBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
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
                Microsoft.UI.ColorHelper.FromArgb(0x50, 0x3C, 0xD4, 0xA0),
                Microsoft.UI.ColorHelper.FromArgb(0x40, 0x4A, 0x7C, 0xF7),
                Microsoft.UI.ColorHelper.FromArgb(0x35, 0xA8, 0x55, 0xF7)),
            "Sunset" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x52, 0xFF, 0x8A, 0x65),
                Microsoft.UI.ColorHelper.FromArgb(0x45, 0xFF, 0xC1, 0x07),
                Microsoft.UI.ColorHelper.FromArgb(0x32, 0xAB, 0x47, 0xBC)),
            "Ocean" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x50, 0x00, 0xBC, 0xD4),
                Microsoft.UI.ColorHelper.FromArgb(0x42, 0x29, 0x62, 0xFF),
                Microsoft.UI.ColorHelper.FromArgb(0x30, 0x26, 0xC6, 0xDA)),
            "Graphite" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x4E, 0x2F, 0x36, 0x40),
                Microsoft.UI.ColorHelper.FromArgb(0x42, 0x4B, 0x55, 0x63),
                Microsoft.UI.ColorHelper.FromArgb(0x30, 0x76, 0x82, 0x92)),
            "Forest" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x52, 0x2E, 0x7D, 0x32),
                Microsoft.UI.ColorHelper.FromArgb(0x40, 0x00, 0x79, 0x6B),
                Microsoft.UI.ColorHelper.FromArgb(0x30, 0x55, 0x8B, 0x2F)),
            "HighContrast" => CreateGradientBrush(
                Microsoft.UI.ColorHelper.FromArgb(0x66, 0x00, 0x00, 0x00),
                Microsoft.UI.ColorHelper.FromArgb(0x58, 0x12, 0x12, 0x12),
                Microsoft.UI.ColorHelper.FromArgb(0x4A, 0x00, 0x78, 0xD7)),
            _ => new SolidColorBrush(Colors.Transparent)
        };
    }

    private static Brush CreateGradientBrush(Windows.UI.Color start, Windows.UI.Color middle, Windows.UI.Color end)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new() { Color = start, Offset = 0 },
                new() { Color = middle, Offset = 0.5 },
                new() { Color = end, Offset = 1 }
            }
        };
    }
}
