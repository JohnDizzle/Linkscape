using LinkScape.Browser;
using Microsoft.UI;
using Microsoft.UI.Reactor;

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
ReactorApp.Run<App>("LinkScape", width: 1200, height: 800);

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
            Component<LinkScape.TabViewPage>()
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
