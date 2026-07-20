
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LinkScape.Browser;

internal static class BrowserConstants
{
    public const string HomeUrlSettingKey = "browser.home.url";
    public const string SaveTabsSettingKey = "browser.tabs.saveOnExit";
    public const string HistoryOpenInNewTabSettingKey = "browser.history.openInNewTab";
    public const string FavoritesOpenInNewTabSettingKey = "browser.favorites.openInNewTab";
    public const string AddressBarOpenDifferentDomainInNewTabSettingKey = "browser.addressBar.openDifferentDomainInNewTab";
    public const string HomeUrl = "https://ntp.msn.com/edge/ntp?locale=en-US&title=New+tab";

    public const string GlyphMenu = "\uE700";
    public const string GlyphBack = "\uE72B";
    public const string GlyphForward = "\uE72A";
    public const string GlyphGo = "\uE72A";
    public const string GlyphHome = "\uE80F";
    public const string GlyphFavorite = "\uE735";
    public const string GlyphFavoriteOutline = "\uE734";
    public const string GlyphAdd = "\uE710";
    public const string GlyphCollections = "\uE8A9";
    public const string GlyphTabs = "\uE8A9";
    public const string GlyphClose = "\uE711";
    public const string GlyphTrash = "\uE74D";
    public const string GlyphRefresh = "\uE72C";
    public const string GlyphSettings = "\uE713";
    public const string GlyphPause = "\uE769";
    public const string GlyphChevronUp = "\uE70E";
    public const string GlyphChevronDown = "\uE70D";
    public const string GlyphMagnifyGlass = "\uE721";
    public const string GlyphChat = "\uE8F2";
    public static FontFamily TextFontFamily => new("Segoe UI");
    public static FontFamily IconFontFamily => new("Segoe Fluent Icons");
    public static Brush LayerFillDefaultBrush => GetBrush("LayerFillColorDefaultBrush");
    public static Brush LayerFillAltBrush => GetBrush("LayerFillColorAltBrush");
    public static Brush LayerOnMicaBaseAltFillColorDefaultBrush => GetBrush("LayerOnMicaBaseAltFillColorDefaultBrush");
    public static Brush CardBackgroundFillColorDefaultBrush => GetBrush("CardBackgroundFillColorDefaultBrush");
    public static Brush SubtleFillColorSecondaryBrush => GetBrush("SubtleFillColorSecondaryBrush");
    public static Brush AccentFillColorDefaultBrush => GetBrush("AccentFillColorDefaultBrush");
    public static Brush AccentFillColorTertiaryBrush => GetBrush("AccentFillColorTertiaryBrush");
    public static Brush SurfaceStrokeColorDefaultBrush => GetBrush("SurfaceStrokeColorDefaultBrush");

    private static Brush GetBrush(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }


    public static readonly TransitionCollection TabTransitions =
    [
        new EntranceThemeTransition
    {
        FromVerticalOffset = 20,
        IsStaggeringEnabled = true
    }
    ];

}
