using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LinkScape.Browser;

internal static class BrowserMaterialTheme
{
    public const string SettingKey = "ui.material.theme";
    public const string DefaultPreset = DefaultThemePreset;
    public const string DefaultThemePreset = "Default";
    public const string FluentPreset = "Fluent";
    public const string MaterialPreset = "Material";
    public const string HighContrastPreset = "HighContrast";
    private const string MonochromePreset = "Monochrome";
    private const string Material8Preset = "Material8";

    public static readonly string[] Presets =
    [
        DefaultThemePreset,
        FluentPreset,
        MaterialPreset,
        HighContrastPreset
    ];

    public static string CurrentPreset =>
        NormalizePreset(SettingsService.GetValueOrDefault(SettingKey, DefaultPreset));

    public static bool IsDefault => string.Equals(CurrentPreset, DefaultThemePreset, StringComparison.Ordinal);

    public static bool IsHighContrast => string.Equals(CurrentPreset, HighContrastPreset, StringComparison.Ordinal);

    public static Brush GlassFillBrush => BrushFor(
        mica: BrowserConstants.LayerFillDefaultBrush,
        fluent: FromArgb(0x24, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0x30, 0xF9, 0xDE, 0xE9),
        highContrast: FromArgb(0xD8, 0x16, 0x16, 0x16));

    public static Brush GlassStrongFillBrush => BrushFor(
        mica: BrowserConstants.LayerFillAltBrush,
        fluent: FromArgb(0x34, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0x42, 0xD9, 0xF2, 0xEA),
        highContrast: FromArgb(0xF0, 0x24, 0x24, 0x24));

    public static Brush GlassStrokeBrush => BrushFor(
        mica: BrowserConstants.SurfaceStrokeColorDefaultBrush,
        fluent: FromArgb(0x64, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0xA8, 0xF8, 0xC7, 0xDA),
        highContrast: FromArgb(0x68, 0xC8, 0xC8, 0xC8));

    public static Brush SelectedStrokeBrush => BrushFor(
        mica: BrowserConstants.AccentFillColorTertiaryBrush,
        fluent: FromArgb(0xCC, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0xF0, 0xBA, 0xE7, 0xFF),
        highContrast: FromArgb(0xA8, 0xFF, 0xFF, 0xFF));

    public static Brush LoadingStrokeBrush => BrushFor(
        mica: BrowserConstants.AccentFillColorTertiaryBrush,
        fluent: FromArgb(0xEC, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0xFF, 0xB5, 0xEC, 0xD8),
        highContrast: FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    public static Brush PillFillBrush => BrushFor(
        mica: BrowserConstants.LayerFillDefaultBrush,
        fluent: FromArgb(0x28, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0x3C, 0xF7, 0xD6, 0xE4),
        highContrast: FromArgb(0xE8, 0x22, 0x22, 0x22));

    public static Brush BadgeFillBrush => BrushFor(
        mica: BrowserConstants.AccentFillColorTertiaryBrush,
        fluent: FromArgb(0xE8, 0xF2, 0xF4, 0xF8),
        material: FromArgb(0xF2, 0xD8, 0xF4, 0xEA),
        highContrast: FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    public static Brush BadgeForegroundBrush => BrushFor(
        mica: new SolidColorBrush(Microsoft.UI.Colors.White),
        fluent: FromArgb(0xFF, 0x12, 0x18, 0x20),
        material: FromArgb(0xFF, 0x16, 0x20, 0x22),
        highContrast: FromArgb(0xFF, 0x00, 0x00, 0x00));

    public static Brush ChatSurfaceBrush => BrushFor(
        mica: new SolidColorBrush(ColorHelper.FromArgb(0xF0, 0x27, 0x27, 0x29)),
        fluent: FromArgb(0xF2, 0x22, 0x24, 0x27),
        material: FromArgb(0xF2, 0x29, 0x27, 0x2D),
        highContrast: FromArgb(0xF4, 0x16, 0x16, 0x16));

    public static Brush ChatUserBubbleBrush => BrushFor(
        mica: BrowserConstants.AccentFillColorTertiaryBrush,
        fluent: FromArgb(0x34, 0xFF, 0xFF, 0xFF),
        material: FromArgb(0x54, 0xF7, 0xC6, 0xDB),
        highContrast: FromArgb(0x18, 0xFF, 0xFF, 0xFF));

    public static Brush ChatAssistantBubbleBrush => BrushFor(
        mica: new SolidColorBrush(ColorHelper.FromArgb(0xF5, 0x2E, 0x2E, 0x31)),
        fluent: FromArgb(0xF3, 0x2C, 0x30, 0x36),
        material: FromArgb(0xF3, 0x2C, 0x2E, 0x35),
        highContrast: FromArgb(0xF4, 0x16, 0x16, 0x16));

    public static Brush CreateActivityStrokeBrush(RotateTransform rotateTransform)
    {
        Color[] colors = CurrentPreset switch
        {
            FluentPreset =>
            [
                FromArgb(0xFF, 0x72, 0xE9, 0xFF),
                FromArgb(0xFF, 0x8A, 0xB4, 0xFF),
                FromArgb(0xFF, 0xC4, 0x9B, 0xFF),
                FromArgb(0xFF, 0xFF, 0x91, 0xD0),
                FromArgb(0xFF, 0x72, 0xE9, 0xFF)
            ],
            MaterialPreset =>
            [
                FromArgb(0xFF, 0xFF, 0x8F, 0xC7),
                FromArgb(0xFF, 0xFF, 0xC4, 0x72),
                FromArgb(0xFF, 0xC7, 0xEF, 0x83),
                FromArgb(0xFF, 0x76, 0xE3, 0xD0),
                FromArgb(0xFF, 0x9B, 0xB7, 0xFF),
                FromArgb(0xFF, 0xFF, 0x8F, 0xC7)
            ],
            HighContrastPreset =>
            [
                FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                FromArgb(0xFF, 0xFF, 0xE8, 0x00),
                FromArgb(0xFF, 0x00, 0xE5, 0xFF),
                FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            ],
            _ =>
            [
                FromArgb(0xFF, 0xFF, 0x4D, 0x6D),
                FromArgb(0xFF, 0xFF, 0xB3, 0x47),
                FromArgb(0xFF, 0xF2, 0xE8, 0x5C),
                FromArgb(0xFF, 0x50, 0xD8, 0x90),
                FromArgb(0xFF, 0x42, 0xB9, 0xFF),
                FromArgb(0xFF, 0xA7, 0x78, 0xFF),
                FromArgb(0xFF, 0xFF, 0x4D, 0xB8),
                FromArgb(0xFF, 0xFF, 0x4D, 0x6D)
            ]
        };

        var stops = new GradientStopCollection();
        for (var index = 0; index < colors.Length; index++)
        {
            stops.Add(new GradientStop
            {
                Color = colors[index],
                Offset = (double)index / (colors.Length - 1)
            });
        }

        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            RelativeTransform = rotateTransform,
            GradientStops = stops
        };
    }

    public static string NormalizePreset(string? preset)
    {
        return preset?.Trim() switch
        {
            DefaultThemePreset => DefaultThemePreset,
            "Mica" => DefaultThemePreset,
            FluentPreset => FluentPreset,
            MaterialPreset => MaterialPreset,
            HighContrastPreset => HighContrastPreset,
            Material8Preset => MaterialPreset,
            MonochromePreset => FluentPreset,
            "High Contrast" => HighContrastPreset,
            "Material 8" => MaterialPreset,
            "8Material" => MaterialPreset,
            "Mono" => FluentPreset,
            _ => DefaultPreset
        };
    }

    private static Brush BrushFor(Brush mica, Color fluent, Color material, Color highContrast)
    {
        return CurrentPreset switch
        {
            FluentPreset => new SolidColorBrush(fluent),
            MaterialPreset => new SolidColorBrush(material),
            HighContrastPreset => new SolidColorBrush(highContrast),
            _ => mica
        };
    }

    private static Color FromArgb(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
}
