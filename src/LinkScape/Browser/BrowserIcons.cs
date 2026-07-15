namespace LinkScape.Browser;

internal static class BrowserIcons
{
    public static Element FluentIcon(string glyph, double size = 14) =>
        TextBlock(glyph)
            .Set(tb =>
            {
                tb.FontFamily = BrowserConstants.IconFontFamily;
                tb.FontSize = size;
            })
            .VAlign(VerticalAlignment.Center)
            .HAlign(HorizontalAlignment.Center);

    public static ButtonElement IconButton(string glyph, Action onClick, string automationName) =>
        Button(FluentIcon(glyph), onClick)
            .AutomationName(automationName)
            .Width(30)
            .Height(30)
            .Padding(0);
}