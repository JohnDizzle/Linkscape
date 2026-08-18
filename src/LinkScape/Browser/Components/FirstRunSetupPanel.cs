using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace LinkScape.Browser.Components;

internal sealed record FirstRunBrowserOption(string Name, int ProfileCount);

internal sealed record FirstRunImportResult(int FavoriteCount, int HistoryCount, int SourceCount);

internal sealed record FirstRunSetupPanelProps(
    string SelectedSearchProviderKey,
    IReadOnlyList<BrowserSearchProvider> SearchProviders,
    IReadOnlyList<FirstRunBrowserOption> BrowserOptions,
    Action<string> OnSelectSearchProvider,
    Func<IReadOnlyList<string>, bool, bool, Task<FirstRunImportResult>> OnImportAsync,
    Action OnComplete);

internal sealed class FirstRunSetupPanel : Component<FirstRunSetupPanelProps>
{
    private const double CompactLayoutBreakpoint = 900;
    private const string LogoPath = "ms-appx:///Assets/StoreLogo.png";
    private const string OnboardingHeroPath = "ms-appx:///Assets/OnboardingGlobeLink.svg";
    private const string UiFontFamily = "Segoe UI Variable Text";
    private const string DisplayFontFamily = "Segoe UI Variable Display";
    private static readonly Brush BrightTextBrush = ColorBrush(0xFF, 0xF8, 0xFA, 0xFF);
    private static readonly Brush MutedTextBrush = ColorBrush(0xD8, 0xC9, 0xD2, 0xE6);
    private static readonly Brush QuietSurfaceBrush = ColorBrush(0xC8, 0x18, 0x1A, 0x27);
    private static readonly Brush SelectedSurfaceBrush = ColorBrush(0xE8, 0x25, 0x21, 0x4A);
    private static readonly Brush QuietStrokeBrush = ColorBrush(0x34, 0x82, 0x8C, 0xAA);
    private static readonly Brush SelectedStrokeBrush = ColorBrush(0xA0, 0x68, 0x78, 0xB8);
    private static readonly Brush AccentStrokeBrush = ColorBrush(0xFF, 0x74, 0xE5, 0xFF);
    private static readonly Brush VioletBrush = ColorBrush(0xFF, 0xA7, 0x8B, 0xFA);

    public override Element Render()
    {
        var selectedBrowserState = UseState<string?>(null);
        var selectedSearchProvider = UseState(Props.SelectedSearchProviderKey);
        var importFavorites = UseState(true);
        var importHistory = UseState(false);
        var isImporting = UseState(false, threadSafe: true);
        var importStatus = UseState(string.Empty, threadSafe: true);
        var useCompactLayout = UseState(false);
        var pillColumns = useCompactLayout.Value ? 2 : 3;
        var selectedBrowserNames = selectedBrowserState.Value is null
            ? Props.BrowserOptions.Select(option => option.Name).ToArray()
            : ParseBrowserSelection(selectedBrowserState.Value);
        var hasImportSelection = selectedBrowserNames.Length > 0 &&
            (importFavorites.Value || importHistory.Value);

        void ToggleBrowser(string browserName)
        {
            var selected = new HashSet<string>(selectedBrowserNames, StringComparer.OrdinalIgnoreCase);
            if (!selected.Add(browserName))
            {
                selected.Remove(browserName);
            }

            selectedBrowserState.Set(string.Join("\n", selected.Order(StringComparer.OrdinalIgnoreCase)));
        }

        async Task SubmitAsync()
        {
            if (isImporting.Value)
            {
                return;
            }

            if (!hasImportSelection)
            {
                Props.OnSelectSearchProvider(selectedSearchProvider.Value);
                Props.OnComplete();
                return;
            }

            isImporting.Set(true);
            importStatus.Set("Bringing your selected browser data into LinkScape...");

            try
            {
                await Props.OnImportAsync(
                    selectedBrowserNames,
                    importFavorites.Value,
                    importHistory.Value);
                Props.OnSelectSearchProvider(selectedSearchProvider.Value);
                Props.OnComplete();
            }
            catch (Exception ex)
            {
                importStatus.Set($"Import could not finish: {ex.Message}");
                isImporting.Set(false);
            }
        }

        var browserPills = Props.BrowserOptions.Count == 0
            ? Border(
                FlexRow(
                    BuildRoundGlyph(BrowserConstants.GlyphWarning, ColorBrush(0xFF, 0x65, 0x55, 0x50)),
                    VStack(2,
                        TextBlock("No browser profiles detected")
                            .Foreground(BrightTextBrush)
                            .FontFamily(UiFontFamily)
                            .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock("You can continue and import later from Command Center.")
                            .Foreground(MutedTextBrush)
                            .FontFamily(UiFontFamily)
                            .TextWrapping(TextWrapping.Wrap)))
                    .VAlign(VerticalAlignment.Center))
                .Padding(12)
                .CornerRadius(8)
                .Background(QuietSurfaceBrush)
                .WithBorder(QuietStrokeBrush)
            : BuildPillRows(
                Props.BrowserOptions.Select(option =>
                    BuildBrowserPill(
                        option,
                        selectedBrowserNames.Contains(option.Name, StringComparer.OrdinalIgnoreCase),
                        () => ToggleBrowser(option.Name))).ToArray(),
                maxPerRow: pillColumns);

        var searchPills = BuildPillRows(
            Props.SearchProviders.Select(provider =>
                BuildSearchProviderPill(
                    provider,
                    string.Equals(
                        provider.Key,
                        selectedSearchProvider.Value,
                        StringComparison.OrdinalIgnoreCase),
                    () => selectedSearchProvider.Set(provider.Key))).ToArray(),
            maxPerRow: pillColumns);

        var setupForm = ScrollViewer(
                VStack(20,
                    BuildFormSection(
                        "1",
                        BrowserConstants.GlyphGlobe,
                        "Bring data from",
                        "Choose one or more detected browsers",
                        browserPills),
                    BuildDivider(),
                    BuildFormSection(
                        "2",
                        BrowserConstants.GlyphImport,
                        "Choose what to import",
                        "Your selection stays on this device",
                        (FlexRow(
                            BuildDataPill(
                                BrowserConstants.GlyphFavorite,
                                "Favorites",
                                "Saved links",
                                importFavorites.Value,
                                () => importFavorites.Set(!importFavorites.Value)),
                            BuildDataPill(
                                BrowserConstants.GlyphHistory,
                                "History",
                                "Recent visits",
                                importHistory.Value,
                                () => importHistory.Set(!importHistory.Value))) with
                        {
                            ColumnGap = 12
                        })),
                    BuildDivider(),
                    BuildFormSection(
                        "3",
                        BrowserConstants.GlyphMagnifyGlass,
                        "Search with",
                        "Choose one provider",
                        searchPills)))
            .Padding(useCompactLayout.Value ? 18 : 24, 8, useCompactLayout.Value ? 18 : 24, 16)
            .Set(scrollViewer =>
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.VerticalScrollMode = ScrollMode.Auto;
                scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
            })
            .Grid(row: 1, column: 1);

        var setupContent = Grid(
            columns: [GridSize.Px(useCompactLayout.Value ? 246 : 310), GridSize.Star()],
            rows: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            BuildLoadingVisual(useCompactLayout.Value)
                .Grid(row: 0, rowSpan: 3, column: 0),
            BuildHeader(Props.OnComplete)
                .Grid(row: 0, column: 1),
            setupForm,
            BuildFooter(
                isImporting.Value,
                importStatus.Value,
                hasImportSelection,
                Props.OnComplete,
                () => _ = SubmitAsync())
                .Grid(row: 2, column: 1));

        return Grid(
            [GridSize.Star()],
            [GridSize.Star()],
            Border(null)
                .Background(CreateOverlayBrush())
                .Grid(row: 0, column: 0),
            Border(setupContent)
                .MaxWidth(1040)
                .MaxHeight(700)
                .Margin(24)
                .CornerRadius(8)
                .Background(CreatePanelBrush())
                .WithBorder(AccentStrokeBrush)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Set(panel => ConfigurePanel(panel, useCompactLayout.Set))
                .Grid(row: 0, column: 0));
    }

    private static Element BuildHeader(Action onClose) =>
        Border(
            FlexRow(
                PersonPicture()
                    .Width(48)
                    .Height(48)
                    .Set(picture => ConfigureProfilePicture(picture, LogoPath, "LinkScape", "LS")),
                VStack(2,
                    (TextBlock("Make LinkScape yours") with
                    {
                        FontSize = 27,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Foreground(BrightTextBrush)
                    .FontFamily(DisplayFontFamily)
                    .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock("A quick setup, then the browser is yours.")
                        .Foreground(MutedTextBrush)
                        .FontFamily(UiFontFamily)
                        .TextWrapping(TextWrapping.Wrap))
                    .Flex(grow: 1, basis: 0),
                Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphClose, 13), onClose)
                    .AutomationName("Close first-time setup")
                    .ToolTip("Close setup")
                    .Width(38)
                    .Height(38)
                    .Padding(0)
                    .CornerRadius(19)
                    .Foreground(BrightTextBrush)
                    .Background(QuietSurfaceBrush)
                    .WithBorder(QuietStrokeBrush))
                .VAlign(VerticalAlignment.Center))
            .Margin(12, 12, 12, 0)
            .Padding(24, 20, 18, 16)
            .Background(ColorBrush(0xA8, 0x12, 0x14, 0x25))
            .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x20, 0x74, 0xE5, 0xFF)))
            .Set(border => ConfigurePanelSection(border, new CornerRadius(8), 10));

    private static Element BuildFooter(
        bool isImporting,
        string status,
        bool hasImportSelection,
        Action onSkip,
        Action onSubmit) =>
        Border(
            FlexRow(
                FlexRow(
                    ProgressRing()
                        .Width(20)
                        .Height(20)
                        .IsActive(isImporting)
                        .IsVisible(isImporting)
                        .Set(ring => ring.Foreground = AccentStrokeBrush),
                    TextBlock(string.IsNullOrWhiteSpace(status)
                            ? "You can change these choices later in Settings."
                            : status)
                        .Foreground(MutedTextBrush)
                        .FontFamily(UiFontFamily)
                        .TextWrapping(TextWrapping.Wrap)
                        .VAlign(VerticalAlignment.Center))
                    .Flex(grow: 1, basis: 0)
                    .VAlign(VerticalAlignment.Center),
                Button("Cancel", onSkip)
                    .AutomationName("Cancel first-time setup")
                    .Height(48)
                    .Padding(22, 0)
                    .CornerRadius(6)
                    .Foreground(BrightTextBrush)
                    .Background(QuietSurfaceBrush)
                    .Set(button => button.FontFamily = new FontFamily(UiFontFamily))
                    .WithBorder(QuietStrokeBrush),
                Button("Save & continue", onSubmit)
                    .AutomationName(hasImportSelection ? "Save choices, import selected data, and continue" : "Save choices and continue")
                    .IsEnabled(!isImporting)
                    .Height(48)
                    .Padding(26, 0)
                    .CornerRadius(6)
                    .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
                    .Background(CreateCoolAccentBrush())
                    .Set(button => button.FontFamily = new FontFamily(UiFontFamily)))
                .VAlign(VerticalAlignment.Center))
            .Margin(12, 0, 12, 12)
            .Padding(24, 16)
            .Background(ColorBrush(0xE8, 0x0F, 0x12, 0x1D))
            .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x20, 0x74, 0xE5, 0xFF)))
            .Set(border => ConfigurePanelSection(border, new CornerRadius(8), -10));

    private static Element BuildLoadingVisual(bool useCompactLayout)
    {
        var heroWidth = useCompactLayout ? 210 : 262;
        var heroHeight = useCompactLayout ? 240 : 300;

        return
        Border(
            VStack(26,
                Border(
                    Image(OnboardingHeroPath)
                        .AccessibilityHidden()
                        .Width(heroWidth)
                        .Height(heroHeight)
                        .Set(ConfigureOnboardingHero))
                    .Width(heroWidth)
                    .Height(heroHeight)
                    .CornerRadius(24)
                    .Background(CreateReverseHeroBrush())
                    .WithBorder(ColorBrush(0x20, 0xFF, 0xFF, 0xFF)),
                VStack(8,
                    (FlexRow(
                        BuildRoundGlyph(BrowserConstants.GlyphChat, VioletBrush),
                        (TextBlock("GET LINKING") with
                        {
                            FontSize = useCompactLayout ? 20 : 22,
                            CharacterSpacing = 35,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Foreground(BrightTextBrush)
                        .FontFamily(DisplayFontFamily)
                        .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)) with
                    {
                        ColumnGap = 10
                    })
                    .HAlign(HorizontalAlignment.Center),
                    TextBlock("Linker keeps your selected browser data within reach.")
                        .Foreground(MutedTextBrush)
                        .FontFamily(UiFontFamily)
                        .TextAlignment(TextAlignment.Center)
                        .TextWrapping(TextWrapping.Wrap)
                        .MaxWidth(useCompactLayout ? 200 : 238)))
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center))
            .Padding(useCompactLayout ? 18 : 24)
            .Background(CreateCoolVisualBrush());
    }

    private static Element BuildFormSection(
        string step,
        string glyph,
        string title,
        string description,
        Element content) =>
        VStack(18,
            (FlexRow(
                Border(TextBlock(step)
                        .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
                        .FontFamily(UiFontFamily)
                        .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center))
                    .Width(28)
                    .Height(28)
                    .CornerRadius(14)
                    .Background(CreateCoolAccentBrush()),
                BuildRoundGlyph(glyph, CreateSectionIconBrush(step)),
                VStack(0,
                    TextBlock(title)
                        .Foreground(BrightTextBrush)
                        .FontFamily(UiFontFamily)
                        .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(description)
                        .Foreground(MutedTextBrush)
                        .FontFamily(UiFontFamily)
                        .TextWrapping(TextWrapping.Wrap)))
                .VAlign(VerticalAlignment.Center)) with
            {
                ColumnGap = 12
            },
            content);

    private static Element BuildBrowserPill(
        FirstRunBrowserOption option,
        bool isSelected,
        Action onClick) =>
        Button(
                (FlexRow(
                    PersonPicture()
                        .Width(32)
                        .Height(32)
                        .Set(picture => ConfigureBrowserPicture(picture, option.Name)),
                    VStack(0,
                        TextBlock(option.Name)
                            .Foreground(BrightTextBrush)
                            .FontFamily(UiFontFamily)
                            .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock(option.ProfileCount == 1 ? "1 profile" : $"{option.ProfileCount} profiles")
                            .Foreground(MutedTextBrush)
                            .FontFamily(UiFontFamily))
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0),
                    BrowserIcons.FluentIcon(
                        isSelected ? BrowserConstants.GlyphCheckMark : BrowserConstants.GlyphAdd,
                        11)
                        .Foreground(isSelected ? AccentStrokeBrush : MutedTextBrush))
                    .VAlign(VerticalAlignment.Center)) with
                {
                    ColumnGap = 12
                },
                onClick)
            .AutomationName($"{(isSelected ? "Deselect" : "Select")} {option.Name} for import")
            .Height(62)
            .Padding(18, 8)
            .CornerRadius(31)
            .Foreground(BrightTextBrush)
            .Background(isSelected ? SelectedSurfaceBrush : QuietSurfaceBrush)
            .WithBorder(isSelected ? SelectedStrokeBrush : QuietStrokeBrush)
            .Flex(grow: 1, basis: 0);

    private static Element BuildDataPill(
        string glyph,
        string title,
        string description,
        bool isSelected,
        Action onClick) =>
        Button(
                (FlexRow(
                    BuildRoundGlyph(glyph, isSelected ? DataIconBrush(glyph) : QuietSurfaceBrush),
                    VStack(0,
                        TextBlock(title)
                            .Foreground(BrightTextBrush)
                            .FontFamily(UiFontFamily)
                            .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock(description)
                            .Foreground(MutedTextBrush)
                            .FontFamily(UiFontFamily))
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0),
                    BrowserIcons.FluentIcon(
                        isSelected ? BrowserConstants.GlyphCheckMark : BrowserConstants.GlyphAdd,
                        11)
                        .Foreground(isSelected ? AccentStrokeBrush : MutedTextBrush))
                    .VAlign(VerticalAlignment.Center)) with
                {
                    ColumnGap = 12
                },
                onClick)
            .AutomationName($"{(isSelected ? "Deselect" : "Select")} {title}")
            .Height(64)
            .Padding(18, 8)
            .CornerRadius(32)
            .Foreground(BrightTextBrush)
            .Background(isSelected ? SelectedSurfaceBrush : QuietSurfaceBrush)
            .WithBorder(isSelected ? SelectedStrokeBrush : QuietStrokeBrush)
            .Flex(grow: 1, basis: 0);

    private static Element BuildSearchProviderPill(
        BrowserSearchProvider provider,
        bool isSelected,
        Action onClick) =>
        Button(
                (FlexRow(
                    PersonPicture()
                        .Width(28)
                        .Height(28)
                        .Set(picture => ConfigureProfilePicture(
                            picture,
                            BrowserSearchProviders.GetFaviconUrl(provider.Key),
                            provider.DisplayName,
                            provider.DisplayName[..1])),
                    TextBlock(provider.DisplayName)
                        .Foreground(BrightTextBrush)
                        .FontFamily(UiFontFamily)
                        .TextWrapping(TextWrapping.Wrap)
                        .Flex(grow: 1, basis: 0),
                    BrowserIcons.FluentIcon(
                        isSelected ? BrowserConstants.GlyphCheckMark : BrowserConstants.GlyphAdd,
                        10)
                        .Foreground(isSelected ? AccentStrokeBrush : MutedTextBrush))
                    .VAlign(VerticalAlignment.Center)) with
                {
                    ColumnGap = 12
                },
                onClick)
            .AutomationName($"Use {provider.DisplayName} for search")
            .Height(52)
            .Padding(18, 7)
            .CornerRadius(26)
            .Foreground(BrightTextBrush)
            .Background(isSelected ? SelectedSurfaceBrush : QuietSurfaceBrush)
            .WithBorder(isSelected ? SelectedStrokeBrush : QuietStrokeBrush)
            .Flex(grow: 1, basis: 0);

    private static Element BuildPillRows(IReadOnlyList<Element> pills, int maxPerRow)
    {
        var rows = pills
            .Select((pill, index) => (pill, index))
            .GroupBy(item => item.index / maxPerRow)
            .Select(group => (FlexRow(group.Select(item => item.pill).ToArray()) with
            {
                ColumnGap = 10
            }))
            .ToArray();

        return VStack(10, rows);
    }

    private static Element BuildRoundGlyph(string glyph, Brush background) =>
        Border(BrowserIcons.FluentIcon(glyph, 13)
                .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White)))
            .Width(32)
            .Height(32)
            .CornerRadius(16)
            .Background(background)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center);

    private static Element BuildDivider() =>
        Border(null)
            .Height(1)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x20, 0x74, 0xE5, 0xFF)));

    private static string[] ParseBrowserSelection(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void ConfigureProfilePicture(
        Microsoft.UI.Xaml.Controls.PersonPicture picture,
        string imageUrl,
        string displayName,
        string initials)
    {
        picture.DisplayName = displayName;
        picture.Initials = initials;
        picture.ProfilePicture = new BitmapImage(new Uri(imageUrl));
        picture.Background = QuietSurfaceBrush;
    }

    private static void ConfigureBrowserPicture(
        Microsoft.UI.Xaml.Controls.PersonPicture picture,
        string browserName)
    {
        picture.DisplayName = browserName;
        picture.Initials = string.IsNullOrWhiteSpace(browserName)
            ? "?"
            : browserName.Trim()[..1].ToUpperInvariant();
        picture.Background = BrowserColor(browserName);
    }

    private static Brush BrowserColor(string browserName) => browserName.Trim().ToLowerInvariant() switch
    {
        "edge" or "microsoft edge" => ColorBrush(0xFF, 0x00, 0x78, 0xD4),
        "chrome" or "google chrome" => ColorBrush(0xFF, 0xDB, 0x44, 0x37),
        "firefox" or "mozilla firefox" => ColorBrush(0xFF, 0xF2, 0x7A, 0x24),
        "brave" => ColorBrush(0xFF, 0xFB, 0x54, 0x2B),
        "vivaldi" => ColorBrush(0xFF, 0xEF, 0x39, 0x37),
        _ => ColorBrush(0xFF, 0x66, 0x55, 0x58)
    };

    private static Brush CreateSectionIconBrush(string step) => step switch
    {
        "1" => ColorBrush(0xFF, 0x18, 0x8F, 0xC7),
        "2" => VioletBrush,
        _ => ColorBrush(0xFF, 0x4B, 0x6F, 0xD8)
    };

    private static Brush DataIconBrush(string glyph) =>
        string.Equals(glyph, BrowserConstants.GlyphHistory, StringComparison.Ordinal)
            ? VioletBrush
            : ColorBrush(0xFF, 0x18, 0xA8, 0xD8);

    private static void ConfigurePanel(
        Microsoft.UI.Xaml.Controls.Border panel,
        Action<bool> setCompactLayout)
    {
        var state = panel.Tag as SetupPanelVisualState;
        if (state is null)
        {
            state = new SetupPanelVisualState();
            panel.Tag = state;
            panel.SizeChanged += (_, args) =>
            {
                var isCompact = args.NewSize.Width < CompactLayoutBreakpoint;
                if (state.IsCompact != isCompact)
                {
                    state.IsCompact = isCompact;
                    state.SetCompactLayout?.Invoke(isCompact);
                }
            };
        }

        state.SetCompactLayout = setCompactLayout;

        if (state.EntranceStarted || !AnimationsEnabled())
        {
            return;
        }

        state.EntranceStarted = true;

        panel.Opacity = 0;
        var transform = new TranslateTransform { Y = 22 };
        panel.RenderTransform = transform;

        var fade = CreateAnimation(0, 1, 0.32);
        Storyboard.SetTarget(fade, panel);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var rise = CreateAnimation(22, 0, 0.42, enableDependentAnimation: true);
        Storyboard.SetTarget(rise, transform);
        Storyboard.SetTargetProperty(rise, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(rise);
        storyboard.Begin();
    }

    private sealed class SetupPanelVisualState
    {
        internal Action<bool>? SetCompactLayout { get; set; }

        internal bool? IsCompact { get; set; }

        internal bool EntranceStarted { get; set; }
    }

    private static void ConfigureOnboardingHero(Microsoft.UI.Xaml.Controls.Image hero)
    {
        hero.Stretch = Stretch.Uniform;
        hero.Source = new SvgImageSource(new Uri(OnboardingHeroPath));

        if (hero.Tag is Storyboard || !AnimationsEnabled())
        {
            return;
        }

        var transform = new TranslateTransform { Y = -5 };
        hero.RenderTransform = transform;

        var floatAnimation = CreateAnimation(-5, 5, 2.8, enableDependentAnimation: true);
        floatAnimation.AutoReverse = true;
        floatAnimation.RepeatBehavior = RepeatBehavior.Forever;
        Storyboard.SetTarget(floatAnimation, transform);
        Storyboard.SetTargetProperty(floatAnimation, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(floatAnimation);
        hero.Tag = storyboard;
        storyboard.Begin();
    }

    private static void ConfigurePanelSection(
        Microsoft.UI.Xaml.Controls.Border section,
        CornerRadius cornerRadius,
        double startX)
    {
        section.CornerRadius = cornerRadius;

        if (section.Tag is Storyboard || !AnimationsEnabled())
        {
            return;
        }

        section.Opacity = 0;
        var transform = new TranslateTransform { X = startX };
        section.RenderTransform = transform;

        var fade = CreateAnimation(0, 1, 0.34);
        Storyboard.SetTarget(fade, section);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = CreateAnimation(startX, 0, 0.4, enableDependentAnimation: true);
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        section.Tag = storyboard;
        storyboard.Begin();
    }

    private static DoubleAnimation CreateAnimation(
        double from,
        double to,
        double durationSeconds,
        bool enableDependentAnimation = false) =>
        new()
        {
            From = from,
            To = to,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(durationSeconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = enableDependentAnimation
        };

    private static Brush CreateOverlayBrush() => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xEE, 0x05, 0x08, 0x12), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xF4, 0x11, 0x0D, 0x22), Offset = 0.52 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xF0, 0x04, 0x0C, 0x18), Offset = 1 }
        }
    };

    private static Brush CreatePanelBrush() => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x10, 0x13, 0x1C), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x18, 0x14, 0x29), Offset = 0.48 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x0D, 0x16, 0x24), Offset = 1 }
        }
    };

    private static Brush CreateReverseHeroBrush() => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(1, 0),
        EndPoint = new Windows.Foundation.Point(0, 1),
        GradientStops =
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xE8, 0x25, 0x1D, 0x4A), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xF4, 0x08, 0x0C, 0x19), Offset = 0.54 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xE8, 0x08, 0x24, 0x31), Offset = 1 }
        }
    };

    private static Brush CreateCoolVisualBrush() => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x0B, 0x18, 0x2B), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1B, 0x12, 0x38), Offset = 0.56 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x08, 0x1A, 0x25), Offset = 1 }
        }
    };

    private static Brush CreateCoolAccentBrush() => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0.5),
        EndPoint = new Windows.Foundation.Point(1, 0.5),
        GradientStops =
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x17, 0xA8, 0xDC), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x66, 0x5B, 0xE8), Offset = 0.55 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xA7, 0x8B, 0xFA), Offset = 1 }
        }
    };

    private static SolidColorBrush ColorBrush(byte alpha, byte red, byte green, byte blue) =>
        new(Microsoft.UI.ColorHelper.FromArgb(alpha, red, green, blue));

    private static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }
}
