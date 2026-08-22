using LinkScape.Browser;
using LinkScape.Models;
using LinkScape.Services.Collections;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.System;
using WinRT.Interop;

namespace LinkScape.Browser.Components;

internal static class BrowserChrome
{
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";
    private const string BackdropGradientPresetDefault = "Default";
    private const int RailToggleDurationMilliseconds = 180;
    private const double RailHeaderHeight = 0;
    private const double CommandCenterBladeHeight = 300;
    private const double CommandCenterFooterHeight = 108;
    private const double CommandCenterCardHeight = 150;
    private const double CompactTabsCardHeight = 84;
    private const double ExpandedCommandCenterRailWidth = 560;
    private const double ActiveTabHeaderMinHeight = 134;
    private const double RailSectionSpacing = 14;
    private const double ExpandedTabItemHeight = 88;
    private const double CollapsedTabItemHeight = 36;
    private const double CollapsedRailWidth = 56;
    private const double TabItemHoverScale = 1.04;
    private const double TabItemHorizontalInset = 6;
    private const double SelectedTabBorderThickness = 1.25;
    internal const double CompactTitleBarBreakpoint = 1180;
    private static Style? _expandedTabItemContainerStyle;
    private static Style? _collapsedTabItemContainerStyle;
    private static readonly HashSet<string> PulsedAppKeys = new(StringComparer.Ordinal);
    
    public static double CollapsedRailWidthDefault { get; private set; } = 400;


    internal sealed class SettingGridItem : IReactorKeyed
    {
        public required string Key { get; init; }

        public string Value { get; set; } = string.Empty;

        string IReactorKeyed.Key => Key;
    }

    private sealed class AddressBarVisualState
    {
        public Microsoft.UI.Xaml.Controls.Border? Chrome { get; set; }

        public Microsoft.UI.Xaml.Controls.Border? Underline { get; set; }
    }

    private sealed class RailVisualState
    {
        public bool IsInitialized { get; set; }

        public Microsoft.UI.Xaml.Media.Animation.Storyboard? WidthStoryboard { get; set; }
    }

    private sealed class TitleBarSizeRegistration
    {
        public SizeChangedEventHandler? Handler { get; init; }
    }

    public static TimeSpan RailToggleDuration => TimeSpan.FromMilliseconds(RailToggleDurationMilliseconds);

    internal static bool UseCompactTitleBar(double width) => width < CompactTitleBarBreakpoint;

    public static Element BuildTitleBar(
        BrowserTab selectedTab,
        BrowserWebViewHostController browserController,
        bool useCompactLayout,
        Action<double> onWidthChanged,
        string addressText,
        string homeUrl,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        bool isTabsCollapsed,
        bool canGoBack,
        bool canGoForward,
        Action onToggleTabs,
        Action onOpenCollections,
        bool isChatOpen,
        Action onToggleChat,
        Action onOpenAiKeyDialog,
        Action onBack,
        Action onRefresh,
        Action onForward,
        Action<string> onAddressChanged,
        Action<string> onSubmitAddress,
        Action<Microsoft.UI.Xaml.Controls.AutoSuggestBox> onAddressBoxReady,
        Action<string> onNavigateCurrentTab,
        Action<string> onOpenAddressInNewTab,
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider,
        Action onSetCurrentPageAsHome,
        Action onToggleFavorite,
        Action onShareCurrentPage,
        // PWA / Web App
        InstallableWebApp? installableWebApp,
        bool isWebAppInstalled,
        Action onInstallWebApp,
        Action onOpenWebApp, 
        Action<string, string> onSaveSettingValue,
        Action<string, bool> onToggleExtension,
        Action onClearCache,
        Action onClearCookies,
        Action onClearBrowsingHistory,
        Action onOpenSelectedTabInNewWindow,
        Action onAddTab,
        Action onCloseTab)
    {
        var leadingControls = (FlexRow(
            IconButton(BrowserConstants.GlyphMenu, onToggleTabs, isTabsCollapsed ? "Expand tabs" : "Collapse tabs to icons", buttonSize: 32, iconSize: 15, useGlass: true),
            useCompactLayout ? null : IconButton(BrowserConstants.GlyphCollections, onOpenCollections, "Open collections", buttonSize: 32, iconSize: 15, useGlass: true),
            useCompactLayout ? null : IconButton(BrowserConstants.GlyphAdd, onAddTab, "Add tab", buttonSize: 32, iconSize: 15, useGlass: true),
            useCompactLayout ? null : IconButton(BrowserConstants.GlyphClose, onCloseTab, "Close active tab", buttonSize: 32, iconSize: 15, useGlass: true)) with
        {
            ColumnGap = 7
        }).Grid(row: 0, column: 0);

        var navigationControls = (FlexRow(
            IconButton(BrowserConstants.GlyphBack, onBack, "Go back", buttonSize: 32, iconSize: 15, useGlass: true).IsEnabled(canGoBack),
            IconButton(BrowserConstants.GlyphForward, onForward, "Go forward", buttonSize: 32, iconSize: 15, useGlass: true).IsEnabled(canGoForward),
            IconButton(BrowserConstants.GlyphRefresh, onRefresh, "Refresh page", buttonSize: 32, iconSize: 15, useGlass: true),
            useCompactLayout ? null : IconButton(BrowserConstants.GlyphShare, onShareCurrentPage, "Share current page snapshot and URL", buttonSize: 32, iconSize: 15, useGlass: true),
            useCompactLayout ? null : IconButton(
                selectedTab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                onToggleFavorite,
                "Toggle favorite",
                buttonSize: 32,
                iconSize: 15,
                useGlass: true)) with
        {
            ColumnGap = 7
        }).Grid(row: 0, column: 1);

        var addressBar = BuildAddressBar(
                selectedTab,
                browserController,
                addressText,
                onAddressChanged,
                onSubmitAddress,
                onAddressBoxReady)
            .MinWidth(useCompactLayout ? 180 : 360)
            .HAlign(HorizontalAlignment.Stretch)
            .Grid(row: 0, column: 2);

        Element trailingControls = useCompactLayout
            ? BuildCompactTitleBarOverflowButton(
                selectedTab,
                homeUrl,
                settingsSnapshot,
                isChatOpen,
                onOpenCollections,
                onAddTab,
                onCloseTab,
                onShareCurrentPage,
                onToggleFavorite,
                onNavigateCurrentTab,
                onSetCurrentPageAsHome,
                installableWebApp,
                isWebAppInstalled,
                onInstallWebApp,
                onOpenWebApp,
                onOpenSelectedTabInNewWindow,
                selectedSearchProviderKey,
                searchProviders,
                onSelectSearchProvider,
                onToggleExtension,
                onToggleChat,
                onOpenAiKeyDialog,
                onOpenAddressInNewTab,
                onSaveSettingValue,
                onClearCache,
                onClearCookies,
                onClearBrowsingHistory)
            : FlexRow(
                IconButton(BrowserConstants.GlyphHome, () => onNavigateCurrentTab(homeUrl), "Go home", buttonSize: 32, iconSize: 15, useGlass: true),
                IconButton(BrowserConstants.GlyphSave, onSetCurrentPageAsHome, "Set current page as home", buttonSize: 32, iconSize: 15, useGlass: true),
                installableWebApp is not null
                    ? isWebAppInstalled
                        ? IconButton("\uE8A7", onOpenWebApp, $"App available: Open {installableWebApp.Name}", buttonSize: 32, iconSize: 15, useGlass: true)
                            .Set(button => StartAppAvailablePulse(
                                button,
                                $"{selectedTab.Id}|{installableWebApp.ManifestUrl}"))
                        : IconButton("\uE896", onInstallWebApp, $"App available: Install {installableWebApp.Name}", buttonSize: 32, iconSize: 15, useGlass: true)
                    : null,
                IconButton(BrowserConstants.GlyphNewWindow, onOpenSelectedTabInNewWindow, "Open active tab in new LinkScape window", buttonSize: 32, iconSize: 15, useGlass: true),
                BuildSearchProviderButton(selectedSearchProviderKey, searchProviders, onSelectSearchProvider),
                BuildExtensionsButton(settingsSnapshot, onToggleExtension),
                IconButton(BrowserConstants.GlyphHeart, () => { }, "Sponsor / Rate", buttonSize: 32, iconSize: 15, useGlass: true)
                    .Set(button => button.Flyout = CreateSponsorFlyout(onOpenAddressInNewTab)),
                IconButton(BrowserConstants.GlyphChat, onToggleChat, isChatOpen ? "Hide chat blade" : "Show chat blade", buttonSize: 32, iconSize: 15, useGlass: true),
                IconButton(BrowserConstants.GlyphSettings, () => { }, "Settings", buttonSize: 32, iconSize: 15, useGlass: true)
                    .Set(button =>
                    {
                        button.Flyout = CreateSettingsFlyout(
                            settingsSnapshot,
                            onSaveSettingValue,
                            onOpenAiKeyDialog,
                            onOpenAddressInNewTab,
                            onClearCache,
                            onClearCookies,
                            onClearBrowsingHistory);
                        AppUpdateService.RegisterFlyoutAnchor(button);
                    })) with
            {
                ColumnGap = 7
            };

        return Border(
            Grid(
                columns: [GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                leadingControls,
                navigationControls,
                addressBar,
                trailingControls.Grid(row: 0, column: 3))
                .HAlign(HorizontalAlignment.Stretch)
                .Set(grid => grid.ColumnSpacing = 7))
        .Padding(8, 6, 8, 6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .HAlign(HorizontalAlignment.Stretch)
        .Flex(shrink: 0)
        .Set(border => ConfigureTitleBarWidthTracking(border, onWidthChanged));
    }

    private static Element BuildCompactTitleBarOverflowButton(
        BrowserTab selectedTab,
        string homeUrl,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        bool isChatOpen,
        Action onOpenCollections,
        Action onAddTab,
        Action onCloseTab,
        Action onShareCurrentPage,
        Action onToggleFavorite,
        Action<string> onNavigateCurrentTab,
        Action onSetCurrentPageAsHome,
        InstallableWebApp? installableWebApp,
        bool isWebAppInstalled,
        Action onInstallWebApp,
        Action onOpenWebApp,
        Action onOpenSelectedTabInNewWindow,
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider,
        Action<string, bool> onToggleExtension,
        Action onToggleChat,
        Action onOpenAiKeyDialog,
        Action<string> onOpenAddressInNewTab,
        Action<string, string> onSaveSettingValue,
        Action onClearCache,
        Action onClearCookies,
        Action onClearBrowsingHistory)
    {
        return IconButton(
            BrowserConstants.GlyphMore,
            () => { },
            "More browser actions",
            buttonSize: 32,
            iconSize: 15,
            useGlass: true)
            .Set(button =>
            {
                var menu = new MenuFlyout();
                menu.Items.Add(CreateOverflowMenuItem("Collections", BrowserConstants.GlyphCollections, onOpenCollections));
                menu.Items.Add(CreateOverflowMenuItem("New tab", BrowserConstants.GlyphAdd, onAddTab));
                menu.Items.Add(CreateOverflowMenuItem("Close active tab", BrowserConstants.GlyphClose, onCloseTab));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateOverflowMenuItem("Share page", BrowserConstants.GlyphShare, onShareCurrentPage));
                menu.Items.Add(CreateOverflowMenuItem(
                    selectedTab.IsFavorite ? "Remove favorite" : "Add favorite",
                    selectedTab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                    onToggleFavorite));
                menu.Items.Add(CreateOverflowMenuItem("Go home", BrowserConstants.GlyphHome, () => onNavigateCurrentTab(homeUrl)));
                menu.Items.Add(CreateOverflowMenuItem("Set current page as home", BrowserConstants.GlyphSave, onSetCurrentPageAsHome));

                if (installableWebApp is not null)
                {
                    menu.Items.Add(CreateOverflowMenuItem(
                        isWebAppInstalled ? $"Open {installableWebApp.Name} as app" : $"Install {installableWebApp.Name}",
                        isWebAppInstalled ? "\uE8A7" : "\uE896",
                        isWebAppInstalled ? onOpenWebApp : onInstallWebApp));
                }

                menu.Items.Add(CreateOverflowMenuItem(
                    "Open active tab in new window",
                    BrowserConstants.GlyphNewWindow,
                    onOpenSelectedTabInNewWindow));
                menu.Items.Add(CreateSearchProviderOverflowSubmenu(
                    selectedSearchProviderKey,
                    searchProviders,
                    onSelectSearchProvider));
                menu.Items.Add(CreateExtensionsOverflowSubmenu(settingsSnapshot, onToggleExtension));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(CreateOverflowMenuItem(
                    isChatOpen ? "Hide Linker" : "Show Linker",
                    BrowserConstants.GlyphChat,
                    onToggleChat));

                var sponsorItem = CreateOverflowMenuItem("Sponsor / Rate", BrowserConstants.GlyphHeart, () => { });
                sponsorItem.Click += (_, _) => ShowFlyoutAfterMenuCloses(
                    menu,
                    CreateSponsorFlyout(onOpenAddressInNewTab),
                    button);
                menu.Items.Add(sponsorItem);

                var settingsItem = CreateOverflowMenuItem("Settings", BrowserConstants.GlyphSettings, () => { });
                settingsItem.Click += (_, _) => ShowFlyoutAfterMenuCloses(
                    menu,
                    CreateSettingsFlyout(
                        settingsSnapshot,
                        onSaveSettingValue,
                        onOpenAiKeyDialog,
                        onOpenAddressInNewTab,
                        onClearCache,
                        onClearCookies,
                        onClearBrowsingHistory),
                    button);
                menu.Items.Add(settingsItem);

                button.Flyout = menu;
                AppUpdateService.RegisterFlyoutAnchor(button);
            });
    }

    private static MenuFlyoutItem CreateOverflowMenuItem(
        string text,
        string glyph,
        Action onClick)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = glyph,
                FontSize = 14
            }
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuFlyoutSubItem CreateSearchProviderOverflowSubmenu(
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider)
    {
        var submenu = new MenuFlyoutSubItem
        {
            Text = $"Search provider: {BrowserSearchProviders.GetByKey(selectedSearchProviderKey).DisplayName}",
            Icon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = BrowserConstants.GlyphMagnifyGlass,
                FontSize = 14
            }
        };

        foreach (var provider in searchProviders)
        {
            var providerKey = provider.Key;
            var item = new MenuFlyoutItem
            {
                Text = provider.DisplayName,
                Icon = new BitmapIcon
                {
                    UriSource = new Uri(BrowserSearchProviders.GetFaviconUrl(providerKey), UriKind.Absolute),
                    ShowAsMonochrome = false
                }
            };
            item.Click += (_, _) => onSelectSearchProvider(providerKey);
            submenu.Items.Add(item);
        }

        return submenu;
    }

    private static MenuFlyoutSubItem CreateExtensionsOverflowSubmenu(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, bool> onToggleExtension)
    {
        var submenu = new MenuFlyoutSubItem
        {
            Text = "Extensions",
            Icon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = BrowserConstants.GlyphExtensions,
                FontSize = 14
            }
        };

        foreach (var extension in BrowserExtensionService.Extensions)
        {
            var enabled = GetBooleanSetting(settingsSnapshot, extension.SettingKey);
            var item = CreateOverflowMenuItem(
                extension.IsAvailable
                    ? $"{(enabled ? "Stop" : "Start")} {extension.DisplayName}"
                    : $"{extension.DisplayName} (coming next)",
                enabled ? BrowserConstants.GlyphStop : BrowserConstants.GlyphPlay,
                () => onToggleExtension(extension.Id, !enabled));
            item.IsEnabled = extension.IsAvailable;
            submenu.Items.Add(item);
        }

        return submenu;
    }

    private static void ShowFlyoutAfterMenuCloses(
        MenuFlyout menu,
        Microsoft.UI.Xaml.Controls.Flyout flyout,
        Microsoft.UI.Xaml.Controls.Button anchor)
    {
        menu.Hide();
        _ = anchor.DispatcherQueue.TryEnqueue(() => flyout.ShowAt(anchor));
    }

    private static void ConfigureTitleBarWidthTracking(
        Microsoft.UI.Xaml.Controls.Border border,
        Action<double> onWidthChanged)
    {
        if (border.Tag is TitleBarSizeRegistration previous && previous.Handler is not null)
        {
            border.SizeChanged -= previous.Handler;
        }

        SizeChangedEventHandler handler = (_, args) => onWidthChanged(args.NewSize.Width);
        border.Tag = new TitleBarSizeRegistration { Handler = handler };
        border.SizeChanged += handler;

        if (border.ActualWidth > 0)
        {
            onWidthChanged(border.ActualWidth);
        }
    }

    private static async Task LaunchStoreReviewAsync()
    {
        try
        {
            var context = StoreContext.GetDefault();

            if (MainWindowActivation.Hwnd != nint.Zero)
            {
                InitializeWithWindow.Initialize(context, MainWindowActivation.Hwnd);
                await context.RequestRateAndReviewAppAsync();
            }
        }
        catch
        {
            try
            {
                var uri = new Uri("ms-windows-store://review/?ProductId=9NLNN451LC7T");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch
            {
            }
        }
    }
    private static void StartAppAvailablePulse(
    Microsoft.UI.Xaml.Controls.Button button,
    string appKey)
    {
        // Reactor may recreate the title-bar button when switching between
        // compact and expanded layouts. Remember the discovery independently
        // of that transient control so the attention pulse runs only once.
        if (!PulsedAppKeys.Add(appKey))
        {
            return;
        }
        // Save the existing glass style values.
        var originalBorderBrush = button.BorderBrush;
        var originalBorderThickness = button.BorderThickness;
        var originalBackground = button.Background;

        var brush =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red);

        button.BorderBrush = brush;
        button.BorderThickness = new Thickness(2);

        var animation =
            new Microsoft.UI.Xaml.Media.Animation.ColorAnimationUsingKeyFrames
            {
                Duration = new Duration(
                    TimeSpan.FromMilliseconds(1500))
            };

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(0)),
                Value = Microsoft.UI.Colors.Red
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(250)),
                Value = Microsoft.UI.Colors.Orange
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(500)),
                Value = Microsoft.UI.Colors.Yellow
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(750)),
                Value = Microsoft.UI.Colors.LimeGreen
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(1000)),
                Value = Microsoft.UI.Colors.DeepSkyBlue
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(1250)),
                Value = Microsoft.UI.Colors.MediumPurple
            });

        animation.KeyFrames.Add(
            new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(1500)),
                Value = Microsoft.UI.Colors.Magenta
            });

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(
            animation,
            brush);

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(
            animation,
            "Color");

        var storyboard =
            new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        storyboard.Children.Add(animation);

        storyboard.Completed += (_, _) =>
        {
            button.BorderBrush = originalBorderBrush;
            button.BorderThickness = originalBorderThickness;
            button.Background = originalBackground;
        };

        storyboard.Begin();
    }
    private static Microsoft.UI.Xaml.Controls.Flyout CreateSponsorFlyout(Action<string> onOpenAddressInNewTab)
    {
        const string repositoryUrl = "https://github.com/JohnDizzle/AI-Agent";
        const string sponsorUrl = "https://paypal.me/johndizzleUS";
        // Header + description
        var header = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "⭐ Support & Rate LinkScape",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14
        };

        var description = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "If you like LinkScape, please rate it in the Microsoft Store or sponsor the project.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            FontSize = 12
        };

        // Buttons (created as locals so we don't rely on Children indices)
        var rateButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "⭐ Rate",
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            MinWidth = 120,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 0)
        };

        var contactButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "✉️ Contact support",
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            MinWidth = 160,
            MinHeight = 36
        };

        var sponsorButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "💖 Sponsor on PayPal",
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            MinWidth = 160,
            MinHeight = 36
        };

        var viewRepoButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "🐙 View GitHub Repository",
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            MinWidth = 160,
            MinHeight = 36
        };

        // Small labelled sections
        var sponsorLabel = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Sponsor",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };

        var sponsorDescription = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Help fund ongoing development and cloud certifications.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            FontSize = 12
        };

        var openSourceLabel = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Open Source",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };

        // Build the layout
        var horizontalRatePanel = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 8,
            Children = { rateButton }
        };

        var content = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 10,
            Width = 320,
            Children =
        {
            header,
            description,
            horizontalRatePanel,
            // divider replacement for Separator
            new Microsoft.UI.Xaml.Controls.Border
            {
                Height = 1,
                Background = BrowserConstants.SurfaceStrokeColorDefaultBrush,
                Margin = new Thickness(0,6,0,6)
            },
            sponsorLabel,
            sponsorDescription,
            contactButton,
            sponsorButton,
            // divider replacement for Separator
            new Microsoft.UI.Xaml.Controls.Border
            {
                Height = 1,
                Background = BrowserConstants.SurfaceStrokeColorDefaultBrush,
                Margin = new Thickness(0,6,0,6)
            },
            openSourceLabel,
            viewRepoButton
        }
        };

        var flyout = new Microsoft.UI.Xaml.Controls.Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = GetLinkerFlyoutPresenterStyle(),
            Content = content
        };

        // Handlers
        rateButton.Click += async (_, _) =>
        {
            await LaunchStoreReviewAsync();
            flyout.Hide();
        };

        contactButton.Click += async (_, _) =>
        {
            await Windows.System.Launcher.LaunchUriAsync(new ($"mailto:fizzledbydizzle@live.com"));
            flyout.Hide();
        };

        sponsorButton.Click += (_, _) =>
        {
            onOpenAddressInNewTab(sponsorUrl);
            flyout.Hide();
        };

        viewRepoButton.Click += (_, _) =>
        {
            onOpenAddressInNewTab(repositoryUrl);
            flyout.Hide();
        };

        return flyout;
    }
    private static Element BuildExtensionsButton(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, bool> onToggleExtension)
    {
        var anyEnabled = BrowserExtensionService.Extensions.Any(extension =>
            extension.IsAvailable &&
            GetBooleanSetting(settingsSnapshot, extension.SettingKey));
        var automationName = anyEnabled ? "Extensions — active" : "Extensions";

        return Button(
            Border(FluentIcon(BrowserConstants.GlyphExtensions, 15))
                .Width(22)
                .Height(22)
                .CornerRadius(7)
                .Background(new SolidColorBrush(Microsoft.UI.Colors.Transparent)),
            () => { })
            .AutomationName(automationName)
            .ToolTip(automationName)
            .Width(32)
            .Height(32)
            .Padding(0)
            .Set(button =>
            {
                button.Style = GetGlassIconButtonStyle();
                ApplyGlassButtonDepth(button);
                button.Flyout = CreateExtensionsFlyout(settingsSnapshot, onToggleExtension);
            });
    }

    private static Microsoft.UI.Xaml.Controls.Flyout CreateExtensionsFlyout(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, bool> onToggleExtension)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout();
        var content = new StackPanel
        {
            Width = 310,
            Spacing = 8
        };

        foreach (var extension in BrowserExtensionService.Extensions)
        {
            var enabled = GetBooleanSetting(settingsSnapshot, extension.SettingKey);
            var button = new Microsoft.UI.Xaml.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = extension.IsAvailable,
                Padding = new Thickness(10, 8, 10, 8),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new FontIcon
                        {
                            FontFamily = BrowserConstants.IconFontFamily,
                            Glyph = enabled ? BrowserConstants.GlyphStop : BrowserConstants.GlyphPlay,
                            FontSize = 14
                        },
                        new StackPanel
                        {
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = extension.IsAvailable
                                        ? $"{(enabled ? "Stop" : "Start")} {extension.DisplayName}"
                                        : $"{extension.DisplayName} (coming next)",
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                                },
                                new TextBlock
                                {
                                    Text = extension.Description,
                                    Opacity = 0.68,
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        }
                    }
                }
            };
            if (extension.IsAvailable)
            {
                button.BorderBrush = new SolidColorBrush(
                    enabled ? Microsoft.UI.Colors.IndianRed : Microsoft.UI.Colors.LimeGreen);
                button.BorderThickness = new Thickness(1.5);
            }
            button.Click += (_, _) =>
            {
                onToggleExtension(extension.Id, !enabled);
                flyout.Hide();
            };
            content.Children.Add(button);
        }

        var availableExtensions = BrowserExtensionService.Extensions
            .Where(extension => extension.IsAvailable)
            .ToArray();
        var runningCount = availableExtensions.Count(extension =>
            GetBooleanSetting(settingsSnapshot, extension.SettingKey));
        content.Children.Add(new Border
        {
            Background = BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush,
            BorderBrush = BrowserConstants.SurfaceStrokeColorDefaultBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{runningCount} of {availableExtensions.Length} extensions running",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = string.Join("  •  ", availableExtensions.Select(extension =>
                            $"{extension.DisplayName}: {(GetBooleanSetting(settingsSnapshot, extension.SettingKey) ? "On" : "Off")}")),
                        Opacity = 0.72
                    },
                }
            }
        });

        flyout.Content = content;
        flyout.FlyoutPresenterStyle = GetLinkerFlyoutPresenterStyle();
        return flyout;
    }

    private static Element BuildAddressBar(
        BrowserTab selectedTab,
        BrowserWebViewHostController browserController,
        string addressText,
        Action<string> onAddressChanged,
        Action<string> onSubmitAddress,
        Action<Microsoft.UI.Xaml.Controls.AutoSuggestBox> onAddressBoxReady)
    {
        Microsoft.UI.Xaml.Controls.Border? addressBarChrome = null;
        Microsoft.UI.Xaml.Controls.Border? addressBarUnderline = null;

        return Border(
            VStack(0,
                (FlexRow(
                    BuildAddressBarFavicon(selectedTab, browserController),
                    Border(
                        AutoSuggestBox(addressText, onAddressChanged, submitted => onSubmitAddress(submitted))
                            .AutomationName("Address Bar")
                            .Set(addressBox => ConfigureAddressBox(addressBox, addressBarChrome, addressBarUnderline, onAddressBoxReady))
                    )
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
                    .MinWidth(0)
                ) with
                {
                    ColumnGap = 8
                })
                .Padding(0)
                .VAlign(VerticalAlignment.Center)
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0),
                Border(null)
                    .Height(2)
                    .Opacity(0)
                    .Margin(12, 0, 12, 0)
                    .Background(BrowserConstants.AccentFillColorDefaultBrush)
                    .Set(border => ConfigureAddressBarUnderline(border, addressBarUnderline = border))
            )
            .HAlign(HorizontalAlignment.Stretch)
            .MinWidth(0)
        )
        .Height(38)
        .Padding(10, 0)
        .CornerRadius(14)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .HAlign(HorizontalAlignment.Stretch)
        .MinWidth(0)
        .Set(border => ConfigureAddressBarChrome(border, addressBarChrome = border));
    }

    private static Element BuildAddressBarFavicon(
        BrowserTab selectedTab,
        BrowserWebViewHostController browserController)
    {
        return Button(
            Border(
                Uri.TryCreate(selectedTab.Url, UriKind.Absolute, out _)
                    ? Image(BrowserUrl.GetFaviconUrl(selectedTab.Url))
                        .AccessibilityHidden()
                        .Width(20)
                        .Height(20)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                        .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
                    : FluentIcon(BrowserConstants.GlyphHome, 17)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center))
                .Width(22)
                .Height(22)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
            .Width(28)
            .Height(28)
            .CornerRadius(9)
            .Background(new SolidColorBrush(Microsoft.UI.Colors.Transparent))
            .Padding(0)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .Flex(shrink: 0),
            () => { })
            .AutomationName("Site controls")
            .ToolTip("View site information and permissions")
            .Width(32)
            .Height(32)
            .Padding(0)
            .VAlign(VerticalAlignment.Center)
            .HAlign(HorizontalAlignment.Center)
            .Flex(shrink: 0)
            .Set(button =>
            {
                button.Style = GetGlassIconButtonStyle();
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.VerticalContentAlignment = VerticalAlignment.Center;
                ApplyGlassButtonDepth(button);
                button.Flyout = SiteControlsFlyout.Create(selectedTab, browserController);
            });
    }

    private static void ConfigureAddressBox(
        Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox,
        Microsoft.UI.Xaml.Controls.Border? addressBarChrome,
        Microsoft.UI.Xaml.Controls.Border? addressBarUnderline,
        Action<Microsoft.UI.Xaml.Controls.AutoSuggestBox> onAddressBoxReady)
    {
        addressBox.PlaceholderText = "Search or enter web address";
        addressBox.Height = 34;
        addressBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        addressBox.MinWidth = 0;
        addressBox.Padding = new Thickness(0, 0, 0, 1);
        addressBox.CornerRadius = new CornerRadius(12);
        addressBox.BorderThickness = new Thickness(0);
        addressBox.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        addressBox.QueryIcon = new Microsoft.UI.Xaml.Controls.FontIcon
        {
            Glyph = BrowserConstants.GlyphMagnifyGlass,
            FontFamily = BrowserConstants.IconFontFamily,
            FontSize = 14
        };
        addressBox.GotFocus -= OnAddressBoxGotFocus;
        addressBox.GotFocus += OnAddressBoxGotFocus;
        addressBox.LostFocus -= OnAddressBoxLostFocus;
        addressBox.LostFocus += OnAddressBoxLostFocus;
        addressBox.TextChanged -= OnAddressBoxTextChanged;
        addressBox.TextChanged += OnAddressBoxTextChanged;

        var visualState = addressBox.Tag as AddressBarVisualState ?? new AddressBarVisualState();
        visualState.Chrome = addressBarChrome;
        visualState.Underline = addressBarUnderline;
        addressBox.Tag = visualState;

        onAddressBoxReady(addressBox);
        UpdateAddressBarVisualState(addressBox);
    }

    private static void ConfigureAddressBarChrome(Microsoft.UI.Xaml.Controls.Border border, Microsoft.UI.Xaml.Controls.Border? addressBarChrome)
    {
        border.BorderThickness = new Thickness(0);
        border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (addressBarChrome is null)
        {
            return;
        }

        border.BorderThickness = addressBarChrome.BorderThickness;
        border.BorderBrush = addressBarChrome.BorderBrush;
    }

    private static void ConfigureAddressBarUnderline(Microsoft.UI.Xaml.Controls.Border border, Microsoft.UI.Xaml.Controls.Border? addressBarUnderline)
    {
        if (border.RenderTransform is not ScaleTransform)
        {
            border.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            border.RenderTransform = new ScaleTransform { ScaleX = 0.6, ScaleY = 1 };
        }

        if (addressBarUnderline is null)
        {
            return;
        }

        border.Opacity = addressBarUnderline.Opacity;
        if (addressBarUnderline.RenderTransform is ScaleTransform sourceTransform && border.RenderTransform is ScaleTransform targetTransform)
        {
            targetTransform.ScaleX = sourceTransform.ScaleX;
        }
    }

    private static void OnAddressBoxGotFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
        {
            UpdateAddressBarVisualState(addressBox);
            addressBox.DispatcherQueue.TryEnqueue(() =>
            {
                var editor = FindAddressBarEditor(addressBox);
                editor?.SelectAll();
            });
        }
    }

    private static void OnAddressBoxLostFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
        {
            UpdateAddressBarVisualState(addressBox);
        }
    }

    private static void OnAddressBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
        {
            UpdateAddressBarVisualState(addressBox);
        }
    }

    private static void UpdateAddressBarVisualState(Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
    {
        if (addressBox.Tag is not AddressBarVisualState state)
        {
            return;
        }

        var isFocused = addressBox.FocusState != Microsoft.UI.Xaml.FocusState.Unfocused;
        var hasText = !string.IsNullOrWhiteSpace(addressBox.Text);

        if (state.Chrome is not null)
        {
            state.Chrome.BorderThickness = isFocused ? new Thickness(1.5) : new Thickness(0);
            state.Chrome.BorderBrush = isFocused
                ? BrowserMaterialTheme.SelectedStrokeBrush
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            state.Chrome.Shadow = isFocused ? new Microsoft.UI.Xaml.Media.ThemeShadow() : null;
        }

        if (state.Underline is not null)
        {
            AnimateAddressBarUnderline(
                state.Underline,
                isFocused ? 1d : hasText ? 0.35d : 0d,
                isFocused ? 1d : hasText ? 0.82d : 0.6d);
        }
    }

    private static Microsoft.UI.Xaml.Controls.TextBox? FindAddressBarEditor(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Microsoft.UI.Xaml.Controls.TextBox editor)
            {
                return editor;
            }

            var descendant = FindAddressBarEditor(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void AnimateAddressBarUnderline(
        Microsoft.UI.Xaml.Controls.Border underline,
        double targetOpacity,
        double targetScaleX)
    {
        if (underline.RenderTransform is not ScaleTransform transform)
        {
            underline.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            underline.RenderTransform = transform = new ScaleTransform { ScaleX = targetScaleX, ScaleY = 1 };
        }

        if (underline.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
        {
            storyboard.Stop();
        }

        var opacityAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(160)),
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnimation, underline);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var scaleAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = targetScaleX,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleAnimation, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleAnimation, "ScaleX");

        var nextStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        nextStoryboard.Children.Add(opacityAnimation);
        nextStoryboard.Children.Add(scaleAnimation);
        underline.Tag = nextStoryboard;
        nextStoryboard.Begin();
    }

    private static Element BuildSearchProviderButton(
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider)
    {
        var selectedProvider = BrowserSearchProviders.GetByKey(selectedSearchProviderKey);
        var flyout = new MenuFlyout();

        foreach (var provider in searchProviders)
        {
            var providerKey = provider.Key;
            var item = new MenuFlyoutItem
            {
                Text = provider.DisplayName,
                Icon = new BitmapIcon
                {
                    UriSource = new Uri(BrowserSearchProviders.GetFaviconUrl(providerKey), UriKind.Absolute),
                    ShowAsMonochrome = false
                }
            };
            item.Click += (_, _) => onSelectSearchProvider(providerKey);
            flyout.Items.Add(item);
        }

        return Button(
            Border(
                Image(BrowserSearchProviders.GetFaviconUrl(selectedProvider.Key))
                    .AccessibilityHidden()
                    .Width(16)
                    .Height(16)
                    .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill).ToolTip("Default Search Provider")
            )
            .Width(22)
            .Height(22)
            .CornerRadius(6)
            .Padding(2),
            () => { })
            .AutomationName("Search provider")
            .ToolTip("Search provider")
            .Width(32)
            .Height(32)
            .Padding(0)
            .Set(button =>
            {
                button.Style = GetGlassIconButtonStyle();
                button.Flyout = flyout;
                ApplyGlassButtonDepth(button);
            })
            .WithKey($"search-provider:{selectedProvider.Key}");
    }

    private static MenuFlyout CreateTabContextFlyout(
        BrowserTab tab,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab,
        Action<string> onOpenTabInNewWindow,
        Action<string, int>? onMoveTab = null,
        int tabIndex = 0,
        int tabCount = 1,
        Func<BrowserTab, string?>? getTabInstalledWebAppName = null,
        Func<BrowserTab, string?>? getTabInstallableWebAppName = null,
        Action<string>? onOpenTabAsWebApp = null,
        Action<string>? onInstallTabWebApp = null)
    {
        return CreateTabContextFlyout(
            tab,
            [],
            null,
            onToggleFavoriteTab,
            onCloseTab,
            onReloadTab,
            onOpenTabInNewWindow,
            onMoveTab,
            tabIndex,
            tabCount,
            getTabInstalledWebAppName,
            getTabInstallableWebAppName,
            onOpenTabAsWebApp,
            onInstallTabWebApp);
    }

    private static MenuFlyout CreateTabContextFlyout(
        BrowserTab tab,
        IReadOnlyList<TabCollection> tabCollections,
        Action<string, string, string>? onAddUrlToCollection,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab,
        Action<string> onOpenTabInNewWindow,
        Action<string, int>? onMoveTab = null,
        int tabIndex = 0,
        int tabCount = 1,
        Func<BrowserTab, string?>? getTabInstalledWebAppName = null,
        Func<BrowserTab, string?>? getTabInstallableWebAppName = null,
        Action<string>? onOpenTabAsWebApp = null,
        Action<string>? onInstallTabWebApp = null)
    {
        var flyout = new MenuFlyout();
        var installedWebAppName = getTabInstalledWebAppName?.Invoke(tab);
        var installableWebAppName = getTabInstallableWebAppName?.Invoke(tab);

        var favoriteItem = new MenuFlyoutItem
        {
            Text = tab.IsFavorite ? "⭐ Remove favorite" : "⭐ Add favorite"
        };
        favoriteItem.Click += (_, _) => onToggleFavoriteTab(tab.Id);

        var reloadItem = new MenuFlyoutItem
        {
            Text = "🔄 Reload"
        };
        reloadItem.Click += (_, _) => onReloadTab(tab.Id);

        var openInNewWindowItem = new MenuFlyoutItem
        {
            Text = "🪟 Open in new window"
        };
        openInNewWindowItem.Click += (_, _) => onOpenTabInNewWindow(tab.Id);

        var moveItem = CreateMoveSubItem(
            "↕️ Move",
            tabIndex,
            tabCount,
            onMoveTab is null ? null : targetIndex => onMoveTab(tab.Id, targetIndex));

        var addToCollectionItem = new MenuFlyoutSubItem
        {
            Text = "🗂️ Add to collection"
        };
        var collections = tabCollections
            .Where(collection => !string.IsNullOrWhiteSpace(collection.Name))
            .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (onAddUrlToCollection is null || collections.Length == 0)
        {
            addToCollectionItem.Items.Add(new MenuFlyoutItem
            {
                Text = "Create a collection first",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var collection in collections)
            {
                var collectionName = collection.Name;
                var item = new MenuFlyoutItem
                {
                    Text = collectionName
                };
                item.Click += (_, _) => onAddUrlToCollection(collectionName, tab.Url, tab.Title);
                addToCollectionItem.Items.Add(item);
            }
        }

        var closeItem = new MenuFlyoutItem
        {
            Text = "❌ Close"
        };
        closeItem.Click += (_, _) => onCloseTab(tab.Id);

        flyout.Items.Add(favoriteItem);
        flyout.Items.Add(reloadItem);
        flyout.Items.Add(openInNewWindowItem);

        if (!string.IsNullOrWhiteSpace(installedWebAppName) && onOpenTabAsWebApp is not null)
        {
            var openAppItem = new MenuFlyoutItem
            {
                Text = $"🚀 Open {installedWebAppName} as app"
            };
            openAppItem.Click += (_, _) => onOpenTabAsWebApp(tab.Id);
            flyout.Items.Add(openAppItem);
        }
        else if (!string.IsNullOrWhiteSpace(installableWebAppName) && onInstallTabWebApp is not null)
        {
            var installAppItem = new MenuFlyoutItem
            {
                Text = $"📲 Install {installableWebAppName} as app"
            };
            installAppItem.Click += (_, _) => onInstallTabWebApp(tab.Id);
            flyout.Items.Add(installAppItem);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(moveItem);
        flyout.Items.Add(addToCollectionItem);
        flyout.Items.Add(closeItem);

        return flyout;
    }

    private static MenuFlyoutSubItem CreateMoveSubItem(
        string text,
        int itemIndex,
        int itemCount,
        Action<int>? onMove)
    {
        var canMoveUp = onMove is not null && itemIndex > 0;
        var canMoveDown = onMove is not null && itemIndex < itemCount - 1;
        var moveItem = new MenuFlyoutSubItem
        {
            Text = text,
            IsEnabled = onMove is not null && itemCount > 1
        };

        AddMoveItem(moveItem, "⬆️ Up", canMoveUp, itemIndex - 1, onMove);
        AddMoveItem(moveItem, "⬇️ Down", canMoveDown, itemIndex + 1, onMove);
        AddMoveItem(moveItem, "⏫ Top", canMoveUp, 0, onMove);
        AddMoveItem(moveItem, "⏬ Bottom", canMoveDown, itemCount - 1, onMove);

        return moveItem;
    }

    private static void AddMoveItem(
        MenuFlyoutSubItem parent,
        string text,
        bool isEnabled,
        int targetIndex,
        Action<int>? onMove)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = isEnabled
        };
        item.Click += (_, _) => onMove?.Invoke(targetIndex);
        parent.Items.Add(item);
    }

    private static MenuFlyout CreateBrowserImportFlyout(
        IReadOnlyDictionary<string, BrowserImportProfile[]> browserProfiles,
        Action onImportAll,
        Action<string> onImportBrowser,
        Action<string, string> onImportBrowserProfile,
        bool isImportRunning)
    {
        var flyout = new MenuFlyout();

        var allBrowsersItem = new MenuFlyoutItem
        {
            Text = isImportRunning ? "Import running…" : "All browsers",
            IsEnabled = !isImportRunning
        };
        allBrowsersItem.Click += (_, _) => onImportAll();
        flyout.Items.Add(allBrowsersItem);

        if (browserProfiles.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = "No supported browsers found",
                IsEnabled = false
            };
            flyout.Items.Add(emptyItem);
            return flyout;
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var (browserName, discoveredProfiles) in browserProfiles.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var profiles = discoveredProfiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.Name))
                .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (profiles.Length <= 1)
            {
                var importSingleProfileBrowserItem = new MenuFlyoutItem
                {
                    Text = browserName,
                    IsEnabled = !isImportRunning
                };
                importSingleProfileBrowserItem.Click += (_, _) =>
                {
                    if (profiles.Length == 1)
                    {
                        onImportBrowserProfile(browserName, profiles[0].Id);
                        return;
                    }

                    onImportBrowser(browserName);
                };
                flyout.Items.Add(importSingleProfileBrowserItem);
                continue;
            }

            var importBrowserItem = new MenuFlyoutSubItem
            {
                Text = browserName,
                IsEnabled = !isImportRunning
            };

            var importAllProfilesItem = new MenuFlyoutItem
            {
                Text = "All profiles",
                IsEnabled = !isImportRunning
            };
            importAllProfilesItem.Click += (_, _) => onImportBrowser(browserName);
            importBrowserItem.Items.Add(importAllProfilesItem);
            importBrowserItem.Items.Add(new MenuFlyoutSeparator());

            foreach (var profile in profiles)
            {
                var importProfileItem = new MenuFlyoutItem
                {
                    Text = profile.Name,
                    IsEnabled = !isImportRunning
                };
                importProfileItem.Click += (_, _) => onImportBrowserProfile(browserName, profile.Id);
                importBrowserItem.Items.Add(importProfileItem);
            }

            flyout.Items.Add(importBrowserItem);
        }

        return flyout;
    }

    public static Element BuildTabRail(
    BrowserTab[] tabs,
    int selectedIndex,
    string selectedTabId,
    bool isTabsCollapsed,
    bool isLoading,
    Action onAddTab,
    Action onExpandTabRail,
    Action onOpenCommandPalette,
    Action<int> onSelect,
    Action<string> onToggleFavoriteTab,
    Action<string> onCloseTabFromContextMenu,
    Action<string> onReloadTab,
    Action<string> onOpenTabInNewWindow,
    Action<string, int> onMoveTab,
    Func<BrowserTab, string?> getTabInstalledWebAppName,
    Func<BrowserTab, string?> getTabInstallableWebAppName,
    Action<string> onOpenTabAsWebApp,
    Action<string> onInstallTabWebApp,
    string activeCommandCenterSection,
    bool isCommandCenterExpanded,
    IReadOnlyList<HistoryItem> mostVisitedItems,
    IReadOnlyList<HistoryItem> recentHistoryItems,
    string historyFilter,
    int historyLimit,
    string historyImportStatus,
    IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
    IReadOnlyList<FavoriteItem> favoriteItems,
    int favoritesLimit,
    IReadOnlyList<TabCollection> tabCollections,
    IReadOnlyList<TabCollectionItem> collectionItems,
    IReadOnlyDictionary<string, string[]> collectionMembership,
    string collectionName,
    string collectionStatus,
    string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        string commandCenterBusyText,
    IReadOnlyDictionary<string, string> settingsSnapshot,
    Action<string, string> onSaveSettingValue,
    Action<string> onHistoryFilterChanged,
    Action onLoadMoreHistory,
    Action<string> onFavoritesFilterChanged,
    Action onLoadMoreFavorites,
    Action<string> onCollectionNameChanged,
    Action onCreateCollection,
    Action onCreateSmartCollections,
    Action<string?> onRefreshCollections,
    Action onDeleteCollection,
    Action onAddCurrentTabToCollection,
    Action<string, string, string> onAddUrlToCollection,
    Action onSetStartupCollection,
    Action onImportHistory,
    Action<string> onImportBrowserHistory,
    Action<string, string> onImportBrowserHistoryProfile,
    Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action<string, string> onImportBrowserFavoritesProfile,
        Action onDeleteAllFavorites,
    Action<string> onOpenHistoryItem,
    Action<string> onOpenHistoryItemInNewTab,
    Action<string> onDeleteHistoryItem,
    Action<string> onOpenFavoriteItem,
    Action<string> onOpenFavoriteItemInNewTab,
    Action<string> onDeleteFavoriteItem,
    Action<string> onOpenCollectionItem,
    Action<string> onOpenCollectionItemInNewTab,
    Action<string> onRemoveCollectionItem,
    Action<string, int> onMoveCollectionItem,
    Action<string> onToggleCommandCenter,
    Action onToggleCommandCenterExpanded,
    bool isRailTabsExpanded,
    Action onMaximizeTabs,
    Action onMinimizeTabs,
    Action onDismissCommandCenter,
    Action onRailTransitionCompleted)
    {
        var selectedTab = tabs.FirstOrDefault(tab => string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal)) ?? tabs[0];
        var isSelectedTabLoading = isLoading && string.Equals(selectedTab.Id, selectedTabId, StringComparison.Ordinal);
        var railCommandCenterSection = string.Equals(activeCommandCenterSection, "Chat", StringComparison.Ordinal)
            ? string.Empty
            : activeCommandCenterSection;
        var collapsedCommandCenterHeight = CommandCenterCardHeight;

        if (!string.IsNullOrWhiteSpace(railCommandCenterSection))
        {
            collapsedCommandCenterHeight += CommandCenterBladeHeight + 4;
        }

        var railWidth = isTabsCollapsed
            ? CollapsedRailWidth
            : isCommandCenterExpanded ? ExpandedCommandCenterRailWidth : CollapsedRailWidthDefault;

        var tabList = (ListView<BrowserTab>(
            tabs,
            (tab, tabIndex) =>
            {
                var isTabLoading = isLoading &&
                    string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);

                var isSelected = string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);

                return (isTabsCollapsed
                    ? BuildCollapsedTabItem(tab, isSelected, isTabLoading, tabIndex, tabs.Length, tabCollections, onAddUrlToCollection, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab, onOpenTabInNewWindow, onMoveTab, getTabInstalledWebAppName, getTabInstallableWebAppName, onOpenTabAsWebApp, onInstallTabWebApp).Padding(0).CornerRadius(12)
                    : BuildExpandedTabItem(tab, isSelected, isTabLoading, tabIndex, tabs.Length, GetCollectionNames(collectionMembership, tab.Url), tabCollections, onAddUrlToCollection, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab, onOpenTabInNewWindow, onMoveTab, getTabInstalledWebAppName, getTabInstallableWebAppName, onOpenTabAsWebApp, onInstallTabWebApp).Padding(4).CornerRadius(12)).WithKey($"{tab.Id}-{isTabsCollapsed}");
            }) with
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelect,
            SelectionMode = ListViewSelectionMode.Single,
        }).Padding(4)
        .Set(listView =>
        {
            //listView.ItemContainerTransitions = BrowserConstants.TabTransitions;    
            listView.IsItemClickEnabled = true;
            listView.BorderThickness = new Thickness(0);
            listView.VerticalAlignment = VerticalAlignment.Stretch;
            listView.ContainerContentChanging -= OnTabListContainerContentChanging;
            listView.ContainerContentChanging += OnTabListContainerContentChanging;
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
                listView,
                isTabsCollapsed ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Disabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(listView, ScrollMode.Enabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(listView, ScrollMode.Disabled);
            listView.ItemContainerStyle = GetTabItemContainerStyle(isTabsCollapsed);
        })
        .MinHeight(0)
        .VAlign(VerticalAlignment.Stretch);

        if (isTabsCollapsed)
        {
            var compactRail = Border(
                    (FlexColumn(
                        BuildCompactRailCommands(
                            tabs,
                            selectedTabId,
                            onAddTab,
                            onExpandTabRail,
                            onSelect,
                            onCloseTabFromContextMenu,
                            onOpenCommandPalette),
                        tabList.Flex(grow: 1, basis: 0)) with
                    {
                        RowGap = 2
                    }))
                .Padding(0)
                .Set(border => ConfigureRailContainer(border, railWidth, onRailTransitionCompleted))
                .Flex(shrink: 0)
                .VAlign(VerticalAlignment.Stretch);

            var compactCommandCenter = BuildCommandCenterBlade(
                    railCommandCenterSection,
                    isCommandCenterExpanded: false,
                    tabs,
                    mostVisitedItems,
                    recentHistoryItems,
                    historyFilter,
                    historyLimit,
                    historyImportStatus,
                    historyImportBrowserProfiles,
                    favoriteItems,
                    favoritesLimit,
                    tabCollections,
                    collectionItems,
                    collectionMembership,
                    collectionName,
                    collectionStatus,
                    favoritesFilter,
                    favoritesImportStatus,
                    favoritesImportBrowserProfiles,
                    isCommandCenterBusy,
                    isCommandCenterHighlighted,
                    settingsSnapshot,
                    onSaveSettingValue,
                    onHistoryFilterChanged,
                    onLoadMoreHistory,
                    onFavoritesFilterChanged,
                    onLoadMoreFavorites,
                    onCollectionNameChanged,
                    onCreateCollection,
                    onCreateSmartCollections,
                    onRefreshCollections,
                    onDeleteCollection,
                    onAddCurrentTabToCollection,
                    onAddUrlToCollection,
                    onSetStartupCollection,
                    onImportHistory,
                    onImportBrowserHistory,
                    onImportBrowserHistoryProfile,
                    onDeleteAllHistory,
                    onImportFavorites,
                    onImportBrowserFavorites,
                    onImportBrowserFavoritesProfile,
                    onDeleteAllFavorites,
                    onOpenHistoryItem,
                    onOpenHistoryItemInNewTab,
                    onDeleteHistoryItem,
                    onOpenFavoriteItem,
                    onOpenFavoriteItemInNewTab,
                    onDeleteFavoriteItem,
                    onOpenCollectionItem,
                    onOpenCollectionItemInNewTab,
                    onRemoveCollectionItem,
                    onMoveCollectionItem,
                    onToggleCommandCenterExpanded,
                    onDismissCommandCenter,
                    showExpandButton: true)
                .Width(420)
                .Height(360)
                .Margin(CollapsedRailWidth + 10, 8, 0, 12)
                .HAlign(HorizontalAlignment.Left)
                .VAlign(VerticalAlignment.Top);

            return Grid(
                    [GridSize.Px(railWidth)],
                    [GridSize.Star()],
                    compactRail.Grid(row: 0, column: 0),
                    compactCommandCenter.Grid(row: 0, column: 0))
                .Width(railWidth)
                .Set(grid => Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(grid, 100))
                .Flex(shrink: 0)
                .VAlign(VerticalAlignment.Stretch);
        }

        bool showCompactTabsCard = !isRailTabsExpanded;

        var openTabsCard = showCompactTabsCard
            ? BuildCompactTabsCard(selectedTab, onMaximizeTabs, onToggleFavoriteTab, isSelectedTabLoading)
                .Height(CompactTabsCardHeight)
                .Flex(grow: 0, shrink: 0, basis: CompactTabsCardHeight).WithKey($"{selectedTab.Id}-compact")
            : BuildRailSectionCard(
                "Open Tabs",
                (FlexColumn(
                    BuildActiveTabHeader(selectedTab, tabs.Length, isSelectedTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab)
                        .Flex(shrink: 0),
                    BuildExpandableTabsList(tabList, onMinimizeTabs)
                        .Flex(grow: 1, shrink: 1, basis: 0)
                ) with
                {
                    RowGap = 12
                })
                .WithKey($"{selectedTab.Id}-expanded")
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0))
                .Flex(grow: 1, shrink: 1, basis: 0);

        return Border(
            FlexColumn(
                Border(null)
                    .Height(RailHeaderHeight)
                    .IsVisible(false),
                openTabsCard,
                BuildCommandCenterHost(
                    railCommandCenterSection,
                    isCommandCenterExpanded,
                    tabs,
                    mostVisitedItems,
                    recentHistoryItems,
                    historyFilter,
                    historyLimit,
                    historyImportStatus,
                    historyImportBrowserProfiles,
                    favoriteItems,
                    favoritesLimit,
                    tabCollections,
                    collectionItems,
                    collectionMembership,
                    collectionName,
                    collectionStatus,
                    favoritesFilter,
                    favoritesImportStatus,
                    favoritesImportBrowserProfiles,
                    isCommandCenterBusy,
                     isCommandCenterHighlighted,
                    commandCenterBusyText,
                    settingsSnapshot,
                    onSaveSettingValue,
                    onHistoryFilterChanged,
                    onLoadMoreHistory,
                    onFavoritesFilterChanged,
                    onLoadMoreFavorites,
                    onCollectionNameChanged,
                    onCreateCollection,
                    onCreateSmartCollections,
                    onRefreshCollections,
                    onDeleteCollection,
                    onAddCurrentTabToCollection,
                    onAddUrlToCollection,
                    onSetStartupCollection,
                    onImportHistory,
                    onImportBrowserHistory,
                    onImportBrowserHistoryProfile,
                    onDeleteAllHistory,
                    onImportFavorites,
                    onImportBrowserFavorites,
                    onImportBrowserFavoritesProfile,
                    onDeleteAllFavorites,
                    onOpenHistoryItem,
                    onOpenHistoryItemInNewTab,
                    onDeleteHistoryItem,
                    onOpenFavoriteItem,
                    onOpenFavoriteItemInNewTab,
                    onDeleteFavoriteItem,
                    onOpenCollectionItem,
                    onOpenCollectionItemInNewTab,
                    onRemoveCollectionItem,
                    onMoveCollectionItem,
                    onToggleCommandCenter,
                    onToggleCommandCenterExpanded,
                    onDismissCommandCenter)
                .Flex(grow: isCommandCenterExpanded || showCompactTabsCard ? 1 : 0, shrink: 1, basis: isCommandCenterExpanded ? 0 : collapsedCommandCenterHeight)
                .VAlign(isCommandCenterExpanded ? VerticalAlignment.Stretch : showCompactTabsCard ? VerticalAlignment.Bottom : VerticalAlignment.Top)
            ) with
            {
                RowGap = RailSectionSpacing
            }
        )
        .Padding(12)
        .Set(border => ConfigureRailContainer(border, railWidth, onRailTransitionCompleted))
        .WithBorder(Theme.SurfaceStroke)
        .Flex(shrink: 0)
        .VAlign(VerticalAlignment.Stretch);
    }

    private static Element BuildCompactRailCommands(
        IReadOnlyList<BrowserTab> tabs,
        string selectedTabId,
        Action onAddTab,
        Action onExpandTabRail,
        Action<int> onSelectTab,
        Action<string> onCloseTab,
        Action onOpenCommandPalette)
    {
        var tabsFlyout = CreateCompactTabsFlyout(
            tabs,
            selectedTabId,
            onExpandTabRail,
            onSelectTab,
            onCloseTab);
        return VStack(4,
                IconButton(
                        BrowserConstants.GlyphTabs,
                        () => { },
                        $"Active tabs, {tabs.Count}",
                        buttonSize: 34,
                        iconSize: 14,
                        useGlass: true)
                    .Set(button => button.Flyout = tabsFlyout)
                    .HAlign(HorizontalAlignment.Center),
                IconButton(
                        BrowserConstants.GlyphLibrary,
                        onOpenCommandPalette,
                        "Search tabs, history, favorites, and collections",
                        buttonSize: 34,
                        iconSize: 14,
                        useGlass: true)
                    .Background(BrowserMaterialTheme.PillFillBrush)
                    .HAlign(HorizontalAlignment.Center),
                IconButton(
                    BrowserConstants.GlyphAdd,
                    onAddTab,
                    "New tab",
                    buttonSize: 34,
                    iconSize: 14,
                    useGlass: true)
                    .HAlign(HorizontalAlignment.Center),
                Border(TextBlock(string.Empty))
                    .Width(28)
                    .Height(1)
                    .Margin(0, 5, 0, 4)
                    .Background(BrowserConstants.SurfaceStrokeColorDefaultBrush))
            .Width(48)
            .Margin(4, 8, 4, 0)
            .HAlign(HorizontalAlignment.Center)
            .Flex(shrink: 0);
    }

    private static Microsoft.UI.Xaml.Controls.Flyout CreateCompactTabsFlyout(
        IReadOnlyList<BrowserTab> tabs,
        string selectedTabId,
        Action onExpandTabRail,
        Action<int> onSelectTab,
        Action<string> onCloseTab)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop,
            FlyoutPresenterStyle = GetLinkerFlyoutPresenterStyle()
        };
        var tabList = new StackPanel
        {
            Spacing = 4
        };

        for (var index = 0; index < tabs.Count; index++)
        {
            var tabIndex = index;
            var tab = tabs[index];
            var isSelected = string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);
            var title = string.IsNullOrWhiteSpace(tab.Title) ? "Untitled tab" : tab.Title.Trim();
            var location = tab.Url;
            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                location = uri.Host;
            }

            var row = new Grid
            {
                ColumnSpacing = 10
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement tabIcon = HasFaviconHost(tab.Url)
                ? new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(BrowserUrl.GetFaviconUrl(tab.Url), UriKind.Absolute)),
                    Width = 20,
                    Height = 20,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                }
                : new FontIcon
                {
                    FontFamily = BrowserConstants.IconFontFamily,
                    Glyph = BrowserConstants.GlyphGlobe,
                    FontSize = 16
                };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(tabIcon, 0);
            row.Children.Add(tabIcon);

            var labels = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxLines = 1
                    },
                    new TextBlock
                    {
                        Text = location,
                        Opacity = 0.66,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxLines = 1
                    }
                }
            };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(labels, 1);
            row.Children.Add(labels);

            var stateIcon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = isSelected ? BrowserConstants.GlyphCheckMark : BrowserConstants.GlyphGo,
                FontSize = 12,
                Opacity = isSelected ? 1 : 0.58
            };
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(stateIcon, 2);
            row.Children.Add(stateIcon);

            var tabButton = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 6, 10, 6),
                Background = isSelected
                    ? BrowserConstants.AccentFillColorTertiaryBrush
                    : BrowserMaterialTheme.PillFillBrush,
                BorderBrush = isSelected
                    ? BrowserMaterialTheme.SelectedStrokeBrush
                    : BrowserMaterialTheme.GlassStrokeBrush,
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                CornerRadius = new CornerRadius(10)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                tabButton,
                isSelected ? $"{title}, active tab" : title);
            tabButton.Click += (_, _) =>
            {
                onSelectTab(tabIndex);
                flyout.Hide();
            };

            var closeButton = new Microsoft.UI.Xaml.Controls.Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Background = BrowserMaterialTheme.PillFillBrush,
                BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
                BorderThickness = new Thickness(1),
                Content = new FontIcon
                {
                    FontFamily = BrowserConstants.IconFontFamily,
                    Glyph = BrowserConstants.GlyphTrash,
                    FontSize = 13
                }
            };
            var closeLabel = $"Close {title}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(closeButton, closeLabel);
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(closeButton, closeLabel);
            closeButton.Click += (_, _) =>
            {
                onCloseTab(tab.Id);
                flyout.Hide();
            };

            var managementRow = new Grid
            {
                ColumnSpacing = 8
            };
            managementRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            managementRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(tabButton, 0);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(closeButton, 1);
            managementRow.Children.Add(tabButton);
            managementRow.Children.Add(closeButton);
            tabList.Children.Add(managementRow);
        }

        var expandButton = new Microsoft.UI.Xaml.Controls.Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 9, 12, 9),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon
                    {
                        FontFamily = BrowserConstants.IconFontFamily,
                        Glyph = BrowserConstants.GlyphFullScreen,
                        FontSize = 14
                    },
                    new TextBlock
                    {
                        Text = "Expand tab rail",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    }
                }
            }
        };
        expandButton.Click += (_, _) =>
        {
            flyout.Hide();
            onExpandTabRail();
        };

        flyout.Content = new StackPanel
        {
            Width = 320,
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Open tabs",
                            FontSize = 18,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = tabs.Count == 1 ? "1 tab in this window" : $"{tabs.Count} tabs in this window",
                            Opacity = 0.68
                        }
                    }
                },
                new ScrollViewer
                {
                    Content = tabList,
                    MaxHeight = Math.Clamp(tabs.Count, 1, 8) * 58,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Enabled,
                    HorizontalScrollMode = ScrollMode.Disabled
                },
                new Border
                {
                    Height = 1,
                    Background = BrowserConstants.SurfaceStrokeColorDefaultBrush
                },
                expandButton
            }
        };

        return flyout;
    }

    private static void ConfigureRailContainer(
        Microsoft.UI.Xaml.Controls.Border border,
        double targetWidth,
        Action onRailTransitionCompleted)
    {
        border.Background = BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush;
        border.CornerRadius = new CornerRadius(0, 10, 10, 0);
        border.MinWidth = 0;

        var state = border.Tag as RailVisualState ?? new RailVisualState();
        border.Tag = state;

        if (!state.IsInitialized)
        {
            state.IsInitialized = true;
            border.Width = targetWidth;
            return;
        }

        var currentWidth = border.ActualWidth > 0
            ? border.ActualWidth
            : double.IsNaN(border.Width)
                ? targetWidth
                : border.Width;

        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            border.Width = targetWidth;
            state.WidthStoryboard?.Stop();
            state.WidthStoryboard = null;
            return;
        }

        state.WidthStoryboard?.Stop();

        var widthAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = new Microsoft.UI.Xaml.Duration(RailToggleDuration),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut
            },
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(widthAnimation, border);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(widthAnimation, "Width");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Completed += (_, _) =>
        {
            border.Width = targetWidth;
            onRailTransitionCompleted();

            if (ReferenceEquals(state.WidthStoryboard, storyboard))
            {
                state.WidthStoryboard = null;
            }
        };

        state.WidthStoryboard = storyboard;
        storyboard.Begin();
    }

    private static Element BuildCommandCenterHost(
        string activeCommandCenterSection,
        bool isCommandCenterExpanded,
        IReadOnlyList<BrowserTab> activeTabs,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        int historyLimit,
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        IReadOnlyList<FavoriteItem> favoriteItems,
        int favoritesLimit,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyList<TabCollectionItem> collectionItems,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        string collectionName,
        string collectionStatus,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        string commandCenterBusyText,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action onLoadMoreHistory,
        Action<string> onFavoritesFilterChanged,
        Action onLoadMoreFavorites,
        Action<string> onCollectionNameChanged,
        Action onCreateCollection,
        Action onCreateSmartCollections,
        Action<string?> onRefreshCollections,
        Action onDeleteCollection,
        Action onAddCurrentTabToCollection,
        Action<string, string, string> onAddUrlToCollection,
        Action onSetStartupCollection,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action<string, string> onImportBrowserHistoryProfile,
        Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action<string, string> onImportBrowserFavoritesProfile,
        Action onDeleteAllFavorites,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
        Action<string> onOpenCollectionItem,
        Action<string> onOpenCollectionItemInNewTab,
        Action<string> onRemoveCollectionItem,
        Action<string, int> onMoveCollectionItem,
        Action<string> onToggleCommandCenter,
        Action onToggleCommandCenterExpanded,
        Action onDismissCommandCenter)
    {
        var blade = BuildCommandCenterBlade(
            activeCommandCenterSection,
            isCommandCenterExpanded,
            activeTabs,
            mostVisitedItems,
            recentHistoryItems,
            historyFilter,
            historyLimit,
            historyImportStatus,
            historyImportBrowserProfiles,
            favoriteItems,
            favoritesLimit,
            tabCollections,
            collectionItems,
            collectionMembership,
            collectionName,
            collectionStatus,
            favoritesFilter,
            favoritesImportStatus,
            favoritesImportBrowserProfiles,
            isCommandCenterBusy,
            isCommandCenterHighlighted,
            settingsSnapshot,
            onSaveSettingValue,
            onHistoryFilterChanged,
            onLoadMoreHistory,
            onFavoritesFilterChanged,
            onLoadMoreFavorites,
            onCollectionNameChanged,
            onCreateCollection,
            onCreateSmartCollections,
            onRefreshCollections,
            onDeleteCollection,
            onAddCurrentTabToCollection,
            onAddUrlToCollection,
            onSetStartupCollection,
            onImportHistory,
            onImportBrowserHistory,
            onImportBrowserHistoryProfile,
            onDeleteAllHistory,
            onImportFavorites,
            onImportBrowserFavorites,
            onImportBrowserFavoritesProfile,
            onDeleteAllFavorites,
            onOpenHistoryItem,
            onOpenHistoryItemInNewTab,
            onDeleteHistoryItem,
            onOpenFavoriteItem,
            onOpenFavoriteItemInNewTab,
            onDeleteFavoriteItem,
            onOpenCollectionItem,
            onOpenCollectionItemInNewTab,
            onRemoveCollectionItem,
            onMoveCollectionItem,
            onToggleCommandCenterExpanded,
            onDismissCommandCenter)
            .MinHeight(0)
            .Flex(grow: isCommandCenterExpanded ? 1 : 0, shrink: 1, basis: 0);

        if (!isCommandCenterExpanded && !string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            blade = blade.Height(CommandCenterBladeHeight);
        }

        return FlexColumn(
            blade,
                BuildRailSectionCard(
                "Command Center",
                BuildCommandCenterFooter(activeCommandCenterSection, onToggleCommandCenter, isCommandCenterBusy, commandCenterBusyText)
                    .Height(CommandCenterFooterHeight), CommandCenterCardHeight))
            .MinHeight(0)
            .Flex(grow: isCommandCenterExpanded ? 1 : 0, shrink: 1, basis: 0);
    }

    private static Element BuildCommandCenterBlade(
        string activeCommandCenterSection,
        bool isCommandCenterExpanded,
        IReadOnlyList<BrowserTab> activeTabs,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        int historyLimit,
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        IReadOnlyList<FavoriteItem> favoriteItems,
        int favoritesLimit,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyList<TabCollectionItem> collectionItems,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        string collectionName,
        string collectionStatus,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action onLoadMoreHistory,
        Action<string> onFavoritesFilterChanged,
        Action onLoadMoreFavorites,
        Action<string> onCollectionNameChanged,
        Action onCreateCollection,
        Action onCreateSmartCollections,
        Action<string?> onRefreshCollections,
        Action onDeleteCollection,
        Action onAddCurrentTabToCollection,
        Action<string, string, string> onAddUrlToCollection,
        Action onSetStartupCollection,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action<string, string> onImportBrowserHistoryProfile,
        Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action<string, string> onImportBrowserFavoritesProfile,
        Action onDeleteAllFavorites,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
        Action<string> onOpenCollectionItem,
        Action<string> onOpenCollectionItemInNewTab,
        Action<string> onRemoveCollectionItem,
        Action<string, int> onMoveCollectionItem,
        Action onToggleCommandCenterExpanded,
        Action onDismissCommandCenter,
        bool showExpandButton = true)
    {
        var shouldHighlight = isCommandCenterBusy || isCommandCenterHighlighted;

        if (string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            return Border(null).IsVisible(false);
        }

        Element content = activeCommandCenterSection switch
        {
            "History" =>  BuildHistoryBladeContent(settingsSnapshot, recentHistoryItems, historyFilter, historyLimit, historyImportStatus, historyImportBrowserProfiles, isCommandCenterBusy, tabCollections, collectionMembership, onHistoryFilterChanged, onLoadMoreHistory, onImportHistory, onImportBrowserHistory, onImportBrowserHistoryProfile, onDeleteAllHistory, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, isCommandCenterExpanded),
            "Recent" => BuildRecentBladeContent(settingsSnapshot, recentHistoryItems, historyLimit, isCommandCenterBusy, tabCollections, collectionMembership, onLoadMoreHistory, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, isCommandCenterExpanded),
            "MostVisited" => BuildMostVisitedBladeContent(settingsSnapshot, mostVisitedItems, isCommandCenterBusy, tabCollections, collectionMembership, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, isCommandCenterExpanded),
            "Favorites" => BuildFavoritesBladeContent(settingsSnapshot, favoriteItems, favoritesFilter, favoritesLimit, favoritesImportStatus, favoritesImportBrowserProfiles, isCommandCenterBusy, tabCollections, collectionMembership, onFavoritesFilterChanged, onLoadMoreFavorites, onImportFavorites, onImportBrowserFavorites, onImportBrowserFavoritesProfile, onDeleteAllFavorites, onOpenFavoriteItem, onOpenFavoriteItemInNewTab, onDeleteFavoriteItem, onAddUrlToCollection, isCommandCenterExpanded),
            "Collections" => BuildCollectionsBladeContent(settingsSnapshot, activeTabs, tabCollections, collectionItems, collectionName, collectionStatus, onCollectionNameChanged, onCreateCollection, onCreateSmartCollections, onRefreshCollections, onDeleteCollection, onAddCurrentTabToCollection, onAddUrlToCollection, onSetStartupCollection, onOpenCollectionItem, onOpenCollectionItemInNewTab, onRemoveCollectionItem, onMoveCollectionItem, isCommandCenterExpanded),
            "Backdrop" => BuildBackdropBladeContent(settingsSnapshot, onSaveSettingValue),
            _ => Border(null)
        };

        var contentViewport = activeCommandCenterSection is "History" or "Favorites" or "Collections"
            ? content
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0)
            : ScrollViewer(content)
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                })
                .VAlign(VerticalAlignment.Stretch)
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0);

        var blade = Border(
            FlexColumn(
                HStack(6,
                    Border(null)
                        .Flex(grow: 1, basis: 0),

                    IconButton(
                        isCommandCenterExpanded ? BrowserConstants.GlyphChevronDown : BrowserConstants.GlyphChevronUp,
                        onToggleCommandCenterExpanded,
                        isCommandCenterExpanded ? "Collapse command center blade" : "Expand command center blade",
                        buttonSize: 24,
                        iconSize: 8,
                        useGlass: true)
                    .IsVisible(showExpandButton)
                    .Margin(2, 0, 0, 0),
                    IconButton(
                        BrowserConstants.GlyphClose,
                        onDismissCommandCenter,
                        "Close command center blade",
                        buttonSize: 24,
                        iconSize: 8,
                        useGlass: true)
                    .Margin(0, 0, 2, 0)
                ).HAlign(HorizontalAlignment.Right),
                contentViewport
            )
            .MinHeight(0)
        )
            .Padding(isCommandCenterExpanded ? 14 : 18)
            .CornerRadius(14)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .Set(ApplyCommandCenterNormalBorder)
            .Margin(0, isCommandCenterExpanded ? 6 : 0, 0, 4)
            .MinHeight(0)
            .Set(border => ApplyCommandCenterBusyState(border, shouldHighlight));

        if (!isCommandCenterExpanded)
        {
            blade = blade.MinHeight(CommandCenterBladeHeight);
        }

        return blade;
    }

    private static Element BuildCommandCenterFooter(
        string activeCommandCenterSection,
        Action<string> onToggleCommandCenter,
        bool isCommandCenterBusy,
        string commandCenterBusyText)
    {
        return Border(
            VStack(8,
                HStack(8,
                    BuildCommandCenterButton("History", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Recent", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("MostVisited", activeCommandCenterSection, onToggleCommandCenter, "Most visited")
                ),
                HStack(8,
                    BuildCommandCenterButton("Favorites", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Collections", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Backdrop", activeCommandCenterSection, onToggleCommandCenter, "Appearance")
                ),
                isCommandCenterBusy
                    ? HStack(8,
                        ProgressRing()
                            .Width(16)
                            .Height(16)
                            .Set(progressRing => progressRing.IsActive = true),
                        TextBlock(string.IsNullOrWhiteSpace(commandCenterBusyText) ? "Working…" : commandCenterBusyText)
                            .Opacity(0.8)
                            .TextTrimming(TextTrimming.CharacterEllipsis))
                    : Border(null).IsVisible(false)
            )
        )
        .Padding(2)
        .Background(BrowserConstants.CardBackgroundFillColorDefaultBrush)
        .Height(CommandCenterFooterHeight);
    }

    private static Microsoft.UI.Xaml.Controls.Flyout CreateSettingsFlyout(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action onOpenAiKeyDialog,
        Action<string> onOpenAddressInNewTab,
        Action onClearCache,
        Action onClearCookies,
        Action onClearBrowsingHistory)
    {
        var homeUrl = settingsSnapshot.TryGetValue(BrowserConstants.HomeUrlSettingKey, out var configuredHomeUrl)
            ? BrowserUrl.Normalize(configuredHomeUrl, BrowserConstants.HomeUrl)
            : BrowserConstants.HomeUrl;
        var saveTabs = GetBooleanSetting(settingsSnapshot, BrowserConstants.SaveTabsSettingKey, true);
        var historyOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var favoritesOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.FavoritesOpenInNewTabSettingKey);
        var addressBarOpenDifferentDomainInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey);
        var automaticDailyUpdateChecks = GetBooleanSetting(settingsSnapshot, AppUpdateService.AutomaticDailyChecksSettingKey, true);
        var selectedBackdropPreset = settingsSnapshot.TryGetValue(BackdropGradientPresetSettingKey, out var configuredBackdropPreset)
            ? NormalizeBackdropGradientPreset(configuredBackdropPreset)
            : BackdropGradientPresetDefault;
        var selectedMaterialTheme = settingsSnapshot.TryGetValue(BrowserMaterialTheme.SettingKey, out var configuredMaterialTheme)
            ? BrowserMaterialTheme.NormalizePreset(configuredMaterialTheme)
            : BrowserMaterialTheme.DefaultPreset;
        var startupCollections = TabCollectionService.GetCollections();
        settingsSnapshot.TryGetValue(TabCollectionService.StartupModeSettingKey, out var startupMode);
        settingsSnapshot.TryGetValue(TabCollectionService.StartupCollectionSettingKey, out var startupCollectionId);
        var openCollectionOnStartup = string.Equals(startupMode, TabCollectionService.StartupModeCollection, StringComparison.OrdinalIgnoreCase);
        var selectedProvider = LinkerAiCredentialService.SelectedProvider;
        var anyAiKeySaved = LinkerAiCredentialService.HasAnyApiKey();

        var content = new StackPanel
        {
            Spacing = 10,
            Width = 420,
            Children =
            {
                new TextBlock
                {
                    Text = "Settings",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = @"Current values from Documents\LinkScapeCache\settings.db.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.76
                },
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Home page", homeUrl),
                    new TextBlock
                    {
                        Text = homeUrl,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    new TextBlock
                    {
                        Text = "The Home button, new tabs, and replacing the last closed tab use this URL. Use the title bar button to capture the current page.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.68
                    },
                    CreateSettingsFlyoutActionButton(
                        "Reset home to default",
                        () => onSaveSettingValue(BrowserConstants.HomeUrlSettingKey, BrowserConstants.HomeUrl)),
                    CreateSettingsOpenSourceSection(onOpenAddressInNewTab)),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Open behavior", glyph: BrowserConstants.GlyphSettings),
                    CreateSettingsFlyoutToggle(
                        "Restore tabs from last session",
                        "When enabled, LinkScape saves open tabs and restores them on the next launch. When disabled, startup opens a fresh home page.",
                        saveTabs,
                        nextValue => onSaveSettingValue(BrowserConstants.SaveTabsSettingKey, nextValue ? "true" : "false")),
                    CreateSettingsFlyoutToggle(
                        "Open collection on startup",
                        "When enabled, LinkScape opens the selected collection instead of the last saved tab session.",
                        openCollectionOnStartup,
                        nextValue =>
                        {
                            if (nextValue && string.IsNullOrWhiteSpace(startupCollectionId) && startupCollections.Count > 0)
                            {
                                onSaveSettingValue(TabCollectionService.StartupCollectionSettingKey, startupCollections[0].Id);
                            }

                            onSaveSettingValue(TabCollectionService.StartupModeSettingKey, nextValue ? TabCollectionService.StartupModeCollection : "tabs");
                        }),
                    CreateSettingsFlyoutCollectionPicker(
                        startupCollections,
                        startupCollectionId,
                        openCollectionOnStartup,
                        onSaveSettingValue),
                    CreateSettingsFlyoutToggle(
                        "History opens in new tab",
                        "History, Recent, and Most visited items open in a new tab by default.",
                        historyOpenInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.HistoryOpenInNewTabSettingKey, nextValue ? "true" : "false")),
                    CreateSettingsFlyoutToggle(
                        "Favorites open in new tab",
                        "Favorite items open in a new tab by default.",
                        favoritesOpenInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.FavoritesOpenInNewTabSettingKey, nextValue ? "true" : "false")),
                    CreateSettingsFlyoutToggle(
                        "Address bar opens different domains in new tab",
                        "When enabled, entering a normalized URL in the address bar opens a new tab if the destination host differs from the current tab.",
                        addressBarOpenDifferentDomainInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey, nextValue ? "true" : "false"))),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("LinkScape updates", glyph: BrowserConstants.GlyphRefresh),
                    new TextBlock
                    {
                        Text = "Keep LinkScape current through Microsoft Store. Update prompts and progress appear beside Settings in the upper-right corner.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutToggle(
                        "Check for updates daily",
                        "When enabled, LinkScape checks once every 24 hours. You choose whether to install an available update now or later.",
                        automaticDailyUpdateChecks,
                        nextValue => onSaveSettingValue(AppUpdateService.AutomaticDailyChecksSettingKey, nextValue ? "true" : "false")),
                    CreateSettingsFlyoutActionButton(
                        "Check for updates now",
                        () => _ = AppUpdateService.CheckForUpdatesNowAsync())),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("First-time setup", glyph: BrowserConstants.GlyphSettings),
                    new TextBlock
                    {
                        Text = "Reopen the setup used to import browser data and choose a search provider.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutActionButton(
                        "Run setup again",
                        () => onSaveSettingValue(
                            FirstRunExperienceService.SettingKey,
                            FirstRunExperienceService.PendingValue))),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Clear browsing data", glyph: BrowserConstants.GlyphTrash),
                    new TextBlock
                    {
                        Text = "Remove data stored by the browser engine. Clearing cookies signs you out of websites.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutActionButton("Clear cached files", onClearCache),
                    CreateSettingsFlyoutActionButton("Clear cookies", onClearCookies),
                    CreateSettingsFlyoutActionButton("Clear browsing history", onClearBrowsingHistory)),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Backdrop tint", glyph: BrowserConstants.GlyphGlobe),
                    new TextBlock
                    {
                        Text = "Adds an optional color wash over the app material.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutBackdropPicker(selectedBackdropPreset, onSaveSettingValue)),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Control theme", glyph: BrowserConstants.GlyphSettings),
                    new TextBlock
                    {
                        Text = "Mica works with every backdrop. Frost favors cool backdrops; Petal favors colorful backdrops. The activity rainbow follows this selection.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutMaterialThemePicker(selectedMaterialTheme, onSaveSettingValue)),
                CreateSettingsFlyoutCard(
                    CreateSettingsFlyoutCardHeader("Linker provider key", glyph: BrowserConstants.GlyphChat),
                    new TextBlock
                    {
                        Text = anyAiKeySaved
                            ? $"Saved provider: {selectedProvider.DisplayName}. The key is stored in Windows Credential Manager, not settings.db."
                            : "No provider key is saved. Add one when you want Linker to answer questions outside the local browser tools.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.76
                    },
                    CreateSettingsFlyoutActionButton(
                        anyAiKeySaved ? "Update provider key" : "Add provider key",
                        onOpenAiKeyDialog),
                    CreateSettingsFlyoutActionButton(
                        $"Remove {selectedProvider.DisplayName} key",
                        () =>
                        {
                            LinkerAiCredentialService.DeleteApiKey(selectedProvider.Id);
                            onSaveSettingValue(LinkerAiCredentialService.ConfiguredSettingKey, LinkerAiCredentialService.HasAnyApiKey() ? "true" : "false");
                        })),
                CreateSettingsFlyoutSettingsList(settingsSnapshot)
            }
        };

        return new Microsoft.UI.Xaml.Controls.Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = GetLinkerFlyoutPresenterStyle(),
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollMode = ScrollMode.Disabled
            }
        };
    }

    private static Style GetLinkerFlyoutPresenterStyle()
    {
        return new Style(typeof(Microsoft.UI.Xaml.Controls.FlyoutPresenter))
        {
            Setters =
            {
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.BackgroundProperty,
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF2, 0x23, 0x23, 0x26))),
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.ForegroundProperty,
                    new SolidColorBrush(Microsoft.UI.Colors.White)),
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.BorderBrushProperty,
                    BrowserConstants.AccentFillColorDefaultBrush),
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.BorderThicknessProperty,
                    new Thickness(1)),
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.CornerRadiusProperty,
                    new CornerRadius(16)),
                new Setter(
                    Microsoft.UI.Xaml.Controls.Control.PaddingProperty,
                    new Thickness(12))
            }
        };
    }

    private static Microsoft.UI.Xaml.Controls.Border CreateSettingsFlyoutCard(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Spacing = 10
        };

        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Microsoft.UI.Xaml.Controls.Border
        {
            Padding = new Thickness(12),
            Background = BrowserConstants.LayerFillDefaultBrush,
            BorderBrush = BrowserConstants.SurfaceStrokeColorDefaultBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = panel,
            Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 1, 10)
        };
    }

    private static UIElement CreateSettingsFlyoutCardHeader(string title, string? url = null, string? glyph = null)
    {
        UIElement icon = !string.IsNullOrWhiteSpace(url) && HasFaviconHost(url)
            ? new Microsoft.UI.Xaml.Controls.Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(BrowserUrl.GetFaviconUrl(url), UriKind.Absolute)),
                Width = 16,
                Height = 16,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(glyph) ? BrowserConstants.GlyphGlobe : glyph,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        return new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.Border
                {
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(4),
                    CornerRadius = new CornerRadius(9),
                    Background = BrowserMaterialTheme.PillFillBrush,
                    BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
                    BorderThickness = new Thickness(1),
                    Child = icon,
                    VerticalAlignment = VerticalAlignment.Center,
                    Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
                    Translation = new System.Numerics.Vector3(0, 1, 8)
                },
                new TextBlock
                {
                    Text = title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private static Microsoft.UI.Xaml.Controls.Button CreateSettingsFlyoutActionButton(string label, Action onClick)
    {
        var button = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = label,
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(10)
        };

        button.Click += (_, _) => onClick();
        return button;
    }

    private static UIElement CreateSettingsOpenSourceSection(Action<string> onOpenAddressInNewTab)
    {
        const string repositoryUrl = "https://github.com/JohnDizzle/AI-Agent";
        const string sponsorUrl = "https://paypal.me/johndizzleUS";
        const string sponsorEmail = "fizzledbydizzle@live.com";

        var openSourceLabel = new TextBlock
        {
            Text = "Open Source: GitHub",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(openSourceLabel, 0);

        var versionBadge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(8),
            Background = BrowserConstants.SubtleFillColorSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"v{AppUpdateService.GetCurrentPackageVersionDisplay()}",
                FontSize = 11,
                Opacity = 0.76
            }
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(versionBadge, 1);

        var whatsNewButton = new Microsoft.UI.Xaml.Controls.HyperlinkButton
        {
            Content = "What’s new",
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        whatsNewButton.Click += (_, _) =>
            onOpenAddressInNewTab(AppUpdateService.GetWhatsNewPageUrl());
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(whatsNewButton, 2);

        var repositoryButton = CreateSettingsIconButton(
            BrowserConstants.GlyphLink,
            "Open GitHub repository",
            () => onOpenAddressInNewTab(repositoryUrl));
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(repositoryButton, 3);

        var openSourceRow = new Microsoft.UI.Xaml.Controls.Grid
        {
            Margin = new Thickness(0, 4, 0, 0),
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto },
                new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto },
                new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                openSourceLabel,
                versionBadge,
                whatsNewButton,
                repositoryButton
            }
        };

        var sponsorTextStack = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "Sponsor",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "Developer continuing cloud certifications.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72
                },
                new TextBlock
                {
                    Text = $"PayPal: {sponsorEmail}",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.76
                }
            }
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(sponsorTextStack, 1);

        var sponsorCard = new Microsoft.UI.Xaml.Controls.Button
        {
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(10),
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Content = new Microsoft.UI.Xaml.Controls.Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(10),
                Child = new Microsoft.UI.Xaml.Controls.Grid
                {
                    ColumnDefinitions =
                    {
                        new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto },
                        new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    ColumnSpacing = 8,
                    Children =
                    {
                        CreateSettingsIconTile(BrowserConstants.GlyphFavorite, "Sponsor"),
                        sponsorTextStack
                    },
                }
            }
        };
        ToolTipService.SetToolTip(sponsorCard, "Open PayPal sponsor page");
        sponsorCard.Click += (_, _) => onOpenAddressInNewTab(sponsorUrl);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                openSourceRow,
                sponsorCard
            }
        };
    }

    private static Microsoft.UI.Xaml.Controls.Button CreateSettingsIconButton(
        string glyph,
        string tooltip,
        Action onClick)
    {
        var button = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Style = GetGlassIconButtonStyle()
        };

        ToolTipService.SetToolTip(button, tooltip);
        ApplyGlassButtonDepth(button);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Microsoft.UI.Xaml.Controls.Border CreateSettingsIconTile(string glyph, string tooltip)
    {
        var tile = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(9),
            Background = BrowserMaterialTheme.BadgeFillBrush,
            BorderBrush = BrowserMaterialTheme.SelectedStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 13,
                Foreground = BrowserMaterialTheme.BadgeForegroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            VerticalAlignment = VerticalAlignment.Top
        };

        ToolTipService.SetToolTip(tile, tooltip);
        return tile;
    }

    private static void OpenExternalUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private static UIElement CreateSettingsFlyoutToggle(
        string title,
        string description,
        bool value,
        Action<bool> onChanged)
    {
        var panel = new StackPanel
        {
            Spacing = 4
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var toggle = new ToggleSwitch
        {
            IsOn = value,
            HorizontalAlignment = HorizontalAlignment.Left,
            OnContent = "On",
            OffContent = "Off"
        };

        toggle.Toggled += (_, _) => onChanged(toggle.IsOn);
        panel.Children.Add(toggle);

        return panel;
    }

    private static UIElement CreateSettingsFlyoutCollectionPicker(
        IReadOnlyList<TabCollection> collections,
        string? selectedCollectionId,
        bool isEnabled,
        Action<string, string> onSaveSettingValue)
    {
        var panel = new StackPanel
        {
            Spacing = 6
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Startup collection",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        if (collections.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Create a collection before choosing one for startup.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.68
            });

            return panel;
        }

        var comboBox = new Microsoft.UI.Xaml.Controls.ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = isEnabled
        };

        var selectedIndex = 0;
        for (var index = 0; index < collections.Count; index++)
        {
            var collection = collections[index];
            comboBox.Items.Add(new Microsoft.UI.Xaml.Controls.ComboBoxItem
            {
                Content = collection.Name,
                Tag = collection.Id
            });

            if (string.Equals(collection.Id, selectedCollectionId, StringComparison.Ordinal))
            {
                selectedIndex = index;
            }
        }

        comboBox.SelectedIndex = selectedIndex;
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not Microsoft.UI.Xaml.Controls.ComboBoxItem item ||
                item.Tag is not string collectionId ||
                string.IsNullOrWhiteSpace(collectionId))
            {
                return;
            }

            onSaveSettingValue(TabCollectionService.StartupCollectionSettingKey, collectionId);
            onSaveSettingValue(TabCollectionService.StartupModeSettingKey, TabCollectionService.StartupModeCollection);
        };

        panel.Children.Add(comboBox);
        panel.Children.Add(new TextBlock
        {
            Text = isEnabled
                ? "This collection will open on the next app launch."
                : "Turn on collection startup to use this selection.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68
        });

        return panel;
    }

    private static UIElement CreateSettingsFlyoutMaterialThemePicker(
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        foreach (var preset in BrowserMaterialTheme.Presets)
        {
            var normalizedPreset = BrowserMaterialTheme.NormalizePreset(preset);
            var isSelected = string.Equals(selectedPreset, normalizedPreset, StringComparison.Ordinal);
            var button = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = GetMaterialThemeDisplayName(normalizedPreset),
                Background = isSelected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush,
                BorderBrush = isSelected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush,
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(13, 6, 13, 6),
                MinWidth = string.Equals(normalizedPreset, BrowserMaterialTheme.HighContrastPreset, StringComparison.Ordinal) ? 114 : 82
            };
            button.Click += (_, _) => onSaveSettingValue(BrowserMaterialTheme.SettingKey, normalizedPreset);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Select material theme: " + normalizedPreset);
            panel.Children.Add(button);
        }

        return panel;
    }

    private static UIElement CreateSettingsFlyoutBackdropPicker(
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var rows = new StackPanel
        {
            Spacing = 8
        };
        var firstRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var secondRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var presets = new[]
        {
            BackdropGradientPresetDefault,
            "Aurora",
            "Sunset",
            "Ocean",
            "Graphite",
            "Forest",
            "HighContrast"
        };

        for (var index = 0; index < presets.Length; index++)
        {
            var normalizedPreset = NormalizeBackdropGradientPreset(presets[index]);
            var isSelected = string.Equals(selectedPreset, normalizedPreset, StringComparison.Ordinal);
            var displayName = string.Equals(normalizedPreset, "HighContrast", StringComparison.Ordinal)
                ? "High contrast"
                : normalizedPreset;
            var button = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = displayName,
                Background = isSelected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush,
                BorderBrush = isSelected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush,
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 6, 11, 6)
            };
            button.Click += (_, _) => onSaveSettingValue(BackdropGradientPresetSettingKey, normalizedPreset);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Select backdrop gradient preset: " + normalizedPreset);
            (index < 4 ? firstRow : secondRow).Children.Add(button);
        }

        rows.Children.Add(firstRow);
        rows.Children.Add(secondRow);
        return rows;
    }

    private static UIElement CreateSettingsFlyoutSettingsList(IReadOnlyDictionary<string, string> settingsSnapshot)
    {
        var settingsItems = settingsSnapshot
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settingsItems.Count == 0)
        {
            return new Microsoft.UI.Xaml.Controls.Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = "No settings were found.",
                    Opacity = 0.7
                }
            };
        }

        var rows = new StackPanel
        {
            Spacing = 8
        };

        foreach (var item in settingsItems)
        {
            rows.Children.Add(new Microsoft.UI.Xaml.Controls.Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = BrowserConstants.LayerFillDefaultBrush,
                BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Key,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(item.Value) ? "(empty)" : item.Value,
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.76
                        }
                    }
                }
            });
        }

        return CreateSettingsFlyoutCard(
            CreateSettingsFlyoutCardHeader("Settings values", glyph: BrowserConstants.GlyphSettings),
            new TextBlock
            {
                Text = "Stored settings from settings.db.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.68,
                Margin = new Thickness(0, 0, 0, 2)
            },
            new ScrollViewer
            {
                Content = rows,
                Height = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                HorizontalScrollMode = ScrollMode.Disabled
            });
    }

    private static Element BuildCommandCenterButton(
        string section,
        string activeCommandCenterSection,
        Action<string> onToggleCommandCenter,
        string? label = null)
    {
        var isActive = string.Equals(activeCommandCenterSection, section, StringComparison.Ordinal);

        return Border(
            Button(label ?? section, () => onToggleCommandCenter(section))
                .CornerRadius(6)
                .Padding(6)
                .Flex(grow: 1, basis: 0)
                .AutomationName(label ?? section)
                .Set(button =>
                {
                    button.Style = GetGlassIconButtonStyle();
                    button.Background = isActive
                        ? BrowserMaterialTheme.GlassStrongFillBrush
                        : BrowserMaterialTheme.PillFillBrush;
                    button.BorderBrush = isActive
                        ? BrowserMaterialTheme.SelectedStrokeBrush
                        : BrowserMaterialTheme.GlassStrokeBrush;
                    button.BorderThickness = new Thickness(isActive ? 1.5 : 1);
                })
        ).WithKey($"cc-button-{ label}-{section}")
        .CornerRadius(6)
        .Flex(grow: 1, basis: 0);
    }

    private static Element BuildRailSectionCard(
        string title,
        Element content,
        double? fixedHeight = null)
    {
        var card = Border(
            FlexColumn(
                TextBlock(title)
                    .Set(textBlock =>
                    {
                        textBlock.FontFamily = BrowserConstants.TextFontFamily;
                        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    })
                    .Flex(shrink: 0),
                Border(content)
                    .Padding(8)
                    .CornerRadius(10)
                    .Background(BrowserConstants.LayerFillDefaultBrush)
                    .WithBorder(Theme.SurfaceStroke)
                    .MinHeight(0)
                    .Flex(grow: 1, basis: 0)
            ) with
            {
                RowGap = 10
            }
        )
        .Padding(12)
        .CornerRadius(16)
        .Background(BrowserConstants.LayerFillAltBrush)
        .WithBorder(Theme.SurfaceStroke)
        .MinHeight(0)
        .Margin(0, 0, 0, 6);

        if (fixedHeight is double height)
        {
            card = card.Height(height);
        }

        return card;
    }

    private static void ApplyCommandCenterBusyState(Microsoft.UI.Xaml.Controls.Border border, bool isBusy)
    {
        if (!isBusy)
        {
            if (border.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
            {
                storyboard.Stop();
                border.Tag = null;
            }

            ApplyCommandCenterNormalBorder(border);
            return;
        }

        border.BorderThickness = new Thickness(2);

        if (border.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard)
        {
            return;
        }

        var rotateTransform = new RotateTransform
        {
            CenterX = 0.5,
            CenterY = 0.5
        };
        border.BorderBrush = BrowserMaterialTheme.CreateActivityStrokeBrush(rotateTransform);

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1.8)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, rotateTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Angle");

        var busyStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        busyStoryboard.Children.Add(animation);
        border.Tag = busyStoryboard;
        busyStoryboard.Begin();
    }

    private static void ApplyCommandCenterNormalBorder(Microsoft.UI.Xaml.Controls.Border border)
    {
        border.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
        border.BorderThickness = new Thickness(1);
    }

    private static Element BuildCompactTabsCard(
        BrowserTab tab,
        Action onShowTabs,
        Action<string> onToggleFavoriteTab,
        bool isLoading)
    {
        return Border(
            (FlexRow(
                BuildTabIcon(tab, isLoading, useTileChrome: false),
                TextBlock(tab.Title)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .TextWrapping(TextWrapping.NoWrap)
                    .Set(textBlock =>
                    {
                        textBlock.FontFamily = BrowserConstants.TextFontFamily;
                        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                        textBlock.MaxLines = 1;
                        textBlock.MinWidth = 0;
                    })
                    .FontSize(13)
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0),
                IconButton(
                    tab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                    () => onToggleFavoriteTab(tab.Id),
                    tab.IsFavorite ? "Remove active tab from favorites" : "Add active tab to favorites",
                    buttonSize: 30,
                    iconSize: 14,
                    useGlass: true)
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 10
            })
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Center)
        )
        .Padding(14, 10)
        .CornerRadius(14)
        .Background(BrowserMaterialTheme.GlassFillBrush)
        .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
        .Set(border =>
        {
            border.DoubleTapped += (_, _) => onShowTabs();
            ToolTipService.SetToolTip(border, "Double-click to show the full tabs list.");
            border.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 1, 8);
            ApplyCompactTabsCardBorderState(border, isLoading);
        });
    }

    private static void ApplyCompactTabsCardBorderState(Microsoft.UI.Xaml.Controls.Border border, bool isLoading)
    {
        if (isLoading)
        {
            ApplyTabItemBorderState(border, false, true);
            return;
        }

        ApplyTabItemBorderState(border, false, false);
        border.BorderThickness = new Thickness(1);
        border.BorderBrush = BrowserMaterialTheme.GlassStrokeBrush;
    }

    private static Element BuildExpandableTabsList(Element tabList, Action onMinimizeTabs)
    {
        return Border(
            tabList
                .Flex(grow: 1, shrink: 1, basis: 0)
                .VAlign(VerticalAlignment.Stretch))
            .Padding(2, 2, 8, 2)
            .MinHeight(0)
            .Flex(grow: 1, shrink: 1, basis: 0)
            .Set(border =>
            {
                border.VerticalAlignment = VerticalAlignment.Stretch;
                border.DoubleTapped += (_, _) => onMinimizeTabs();
                ToolTipService.SetToolTip(border, "Double-click to collapse to the compact Tabs card.");
            });
    }

    private static Element BuildActiveTabHeader(
        BrowserTab tab,
        int tabCount,
        bool isLoading,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        return Border(
            VStack(12,
                (FlexRow(
                    VStack(2,
                        TextBlock("Active Tab")
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock($"{tabCount} tab{(tabCount == 1 ? string.Empty : "s")} in session")
                            .Opacity(0.72)
                            .FontSize(11)
                    )
                    .Flex(grow: 1, basis: 0),
                    IconButton(
                        tab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                        () => onToggleFavoriteTab(tab.Id),
                        tab.IsFavorite ? "Remove active tab from favorites" : "Add active tab to favorites",
                        buttonSize: 28,
                        iconSize: 14,
                        useGlass: true)
                ) with
                {
                    ColumnGap = 8
                })
                .HAlign(HorizontalAlignment.Stretch),
                (FlexRow(
                    BuildTabIcon(tab, isLoading, useTileChrome: false),
                    VStack(3,
                        TextBlock(tab.Title)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap)
                            .Set(textBlock =>
                            {
                                textBlock.FontFamily = BrowserConstants.TextFontFamily;
                                textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                                textBlock.MaxLines = 1;
                                textBlock.MinWidth = 0;
                            }),
                        TextBlock("Active tab")
                            .Opacity(0.66)
                            .FontSize(11))
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0),
                    HStack(6,
                        IconButton(BrowserConstants.GlyphRefresh, () => onReloadTab(tab.Id), "Reload active tab", buttonSize: 30, iconSize: 13, useGlass: true),
                        IconButton(BrowserConstants.GlyphTrash, () => onCloseTab(tab.Id), "Close active tab", buttonSize: 30, iconSize: 13, useGlass: true))
                    .Flex(shrink: 0)) with
                {
                    ColumnGap = 10
                })
                .HAlign(HorizontalAlignment.Stretch),
                HStack(8,
                    BuildTabMetricPill("Session", FormatTabSessionAge(tab.DateTime)),
                    BuildTabMetricPill("Opened", tab.DateTime.ToString("g")))
            )
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(14)
        .CornerRadius(14)
        .Background(BrowserMaterialTheme.GlassFillBrush)
        .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
        .MinHeight(ActiveTabHeaderMinHeight)
        .HAlign(HorizontalAlignment.Stretch)
        .Set(border =>
        {
            border.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 1, 8);
        });
    }

    private static Element BuildTabMetricPill(string label, string value)
    {
        return Border(
            VStack(2,
                TextBlock(label)
                    .Opacity(0.62)
                    .FontSize(10),
                TextBlock(value)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .TextWrapping(TextWrapping.NoWrap)
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
            )
        )
        .Padding(8, 6)
        .CornerRadius(8)
        .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
        .Flex(grow: 1, basis: 0);
    }

    private static string FormatTabSessionAge(DateTime openedAt)
    {
        var elapsed = DateTime.Now - openedAt;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        return $"{Math.Max((int)elapsed.TotalSeconds, 0)}s";
    }

    private static Element BuildHistoryBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        int historyLimit,
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        bool isImportRunning,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        Action<string> onHistoryFilterChanged,
        Action onLoadMoreHistory,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action<string, string> onImportBrowserHistoryProfile,
        Action onDeleteAllHistory,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string, string, string> onAddUrlToCollection,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var historyItems = BuildGroupedHistoryItems(
            recentHistoryItems,
            item => BuildHistoryListItem(item, tabCollections, GetCollectionNames(collectionMembership, item.Url), onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, openInNewTabByDefault))
            .ToArray();

        var header = VStack(10,
            TextBlock("History")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            AutoSuggestBox(historyFilter, onHistoryFilterChanged, submitted => onHistoryFilterChanged(submitted))
                .AutomationName("History Filter")
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0) with
            {
                PlaceholderText = "Filter history"
            },
            HStack(8,
                Border(null)
                    .Flex(grow: 1, basis: 0),
                Button("Import", () => { })
                    .IsEnabled(!isImportRunning)
                    .Set(button => button.Flyout = CreateBrowserImportFlyout(
                        historyImportBrowserProfiles,
                        onImportHistory,
                        onImportBrowserHistory,
                        onImportBrowserHistoryProfile,
                        isImportRunning)),
                Button("Delete all history", onDeleteAllHistory)
                    .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                    .AutomationName("Delete all history"))
                .HAlign(HorizontalAlignment.Stretch));

        var body = isImportRunning
            ? BuildCommandCenterLoadingState(
                "Gathering history items...",
                BuildCommandCenterLoadingRows(4).ToArray())
            : historyItems.Length == 0
            ? Border(
                TextBlock("No history items.")
                    .Opacity(0.7)
            )
            .Padding(8, 4)
            : Border(
                VStack(10,
                    VStack(0, historyItems)
                        .HAlign(HorizontalAlignment.Stretch),
                    BuildHistoryPagingFooter(recentHistoryItems.Count, historyLimit, onLoadMoreHistory))
                    .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(8, 4)
            .HAlign(HorizontalAlignment.Stretch)
            .MinWidth(0);

        return FlexColumn(
            header,
            string.IsNullOrWhiteSpace(historyImportStatus)
                ? Border(null).IsVisible(false)
                : Border(
                    TextBlock(historyImportStatus)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
            ScrollViewer(body)
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                })
                .VAlign(VerticalAlignment.Stretch)
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0)) with
        {
            RowGap = 12
        };
    }

    private static IEnumerable<Element> BuildGroupedHistoryItems(
        IReadOnlyList<HistoryItem> historyItems,
        Func<HistoryItem, Element> buildItem)
    {
        string? previousGroup = null;

        foreach (var historyItem in historyItems)
        {
            var groupLabel = GetHistoryGroupLabel(historyItem.LastVisitedAt);

            if (!string.Equals(previousGroup, groupLabel, StringComparison.Ordinal))
            {
                previousGroup = groupLabel;
                yield return BuildHistoryGroupHeader(groupLabel);
            }

            yield return buildItem(historyItem);
        }
    }

    private static string GetHistoryGroupLabel(DateTime lastVisitedAt)
    {
        var localVisitedAt = lastVisitedAt.Kind == DateTimeKind.Unspecified
            ? lastVisitedAt
            : lastVisitedAt.ToLocalTime();
        var today = DateTime.Today;

        if (localVisitedAt.Date == today)
        {
            return "Today";
        }

        var firstDayOfWeek = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var currentWeekStart = today;

        while (currentWeekStart.DayOfWeek != firstDayOfWeek)
        {
            currentWeekStart = currentWeekStart.AddDays(-1);
        }

        if (localVisitedAt.Date >= currentWeekStart)
        {
            return "This Week";
        }

        return localVisitedAt.ToString("MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static Element BuildHistoryGroupHeader(string title)
    {
        return Border(
            TextBlock(title)
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                .Opacity(0.76)
        )
        .Padding(6, 12, 6, 6)
        .HAlign(HorizontalAlignment.Stretch)
        .WithKey($"history-group:{title}");
    }

    private static Element BuildHistoryPagingFooter(int loadedCount, int historyLimit, Action onLoadMoreHistory)
    {
        var canLoadMore = loadedCount >= historyLimit && historyLimit < 2500;

        return Border(
            HStack(10,
                TextBlock($"Loaded {loadedCount:n0} history items")
                    .Opacity(0.72)
                    .VAlign(VerticalAlignment.Center)
                    .Flex(grow: 1, basis: 0),
                Button("Load more", onLoadMoreHistory)
                    .IsEnabled(canLoadMore)
                    .AutomationName("Load more history")
                    .ToolTip(canLoadMore ? "Load older history items" : "No more loaded in this page"))
            .HAlign(HorizontalAlignment.Stretch))
            .Padding(8, 6)
            .CornerRadius(10)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element BuildRecentBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        int historyLimit,
        bool isLoading,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        Action onLoadMoreHistory,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string, string, string> onAddUrlToCollection,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var recentItems = recentHistoryItems
            .Select(item => BuildHistoryListItem(item, tabCollections, GetCollectionNames(collectionMembership, item.Url), onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, openInNewTabByDefault))
            .ToArray();

        return VStack(10,
            TextBlock("Recent")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            isLoading
                ? BuildCommandCenterLoadingState(
                    "Gathering recent items…",
                    BuildCommandCenterLoadingRows(3).ToArray())
                : recentItems.Length == 0
                ? Border(
                    TextBlock("No recent items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : Border(
                    VStack(10,
                        VStack(0, recentItems)
                            .HAlign(HorizontalAlignment.Stretch),
                        BuildHistoryPagingFooter(recentHistoryItems.Count, historyLimit, onLoadMoreHistory))
                        .HAlign(HorizontalAlignment.Stretch)
                )
                .Padding(8, 4)
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0));
    }

    private static Element BuildMostVisitedBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        bool isLoading,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string, string, string> onAddUrlToCollection,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var topItems = mostVisitedItems.ToArray();
        var rows = new List<Element>();
        var columnCount = isCommandCenterExpanded ? 3 : 2;

        for (var index = 0; index < topItems.Length; index += columnCount)
        {
            var cards = new List<Element>();

            for (var column = index; column < Math.Min(index + columnCount, topItems.Length); column++)
            {
                cards.Add(BuildMostVisitedItem(topItems[column], tabCollections, GetCollectionNames(collectionMembership, topItems[column].Url), onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, onAddUrlToCollection, openInNewTabByDefault)
                    .Flex(grow: 1, basis: 0));
            }

            while (cards.Count < columnCount)
            {
                cards.Add(Border(null)
                    .Width(100)
                    .Height(92)
                    .Flex(grow: 1, basis: 0));
            }

            rows.Add(HStack(isCommandCenterExpanded ? 10 : 8, cards.ToArray()));
        }

        return VStack(isCommandCenterExpanded ? 18 : 20,
            TextBlock("Most visited")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            isLoading
                ? BuildCommandCenterLoadingState(
                    "Gathering most visited items…",
                    BuildCommandCenterLoadingGrid(4).ToArray())
                : rows.Count == 0
                ? Border(
                    TextBlock("No most-visited items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : VStack(isCommandCenterExpanded ? 10 : 14, rows.ToArray())
                    .Padding(0, 0, isCommandCenterExpanded ? 4 : 8, 0))
            .VAlign(isCommandCenterExpanded ? VerticalAlignment.Top : VerticalAlignment.Center);
    }

    private static Element BuildFavoritesBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        int favoritesLimit,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isImportRunning,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyDictionary<string, string[]> collectionMembership,
        Action<string> onFavoritesFilterChanged,
        Action onLoadMoreFavorites,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action<string, string> onImportBrowserFavoritesProfile,
        Action onDeleteAllFavorites,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
        Action<string, string, string> onAddUrlToCollection,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.FavoritesOpenInNewTabSettingKey);
        var favoriteRows = new List<Element>();

        for (var index = 0; index < favoriteItems.Count; index++)
        {
            favoriteRows.Add(BuildFavoriteTabItem(favoriteItems[index], tabCollections, GetCollectionNames(collectionMembership, favoriteItems[index].Url), onOpenFavoriteItem, onOpenFavoriteItemInNewTab, onDeleteFavoriteItem, onAddUrlToCollection, openInNewTabByDefault));
        }

        var header = VStack(10,
            TextBlock("Favorites")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            AutoSuggestBox(favoritesFilter, onFavoritesFilterChanged, submitted => onFavoritesFilterChanged(submitted))
                .AutomationName("Favorites Filter")
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0) with
            {
                PlaceholderText = "Filter favorites"
            },
            HStack(8,
                Border(null)
                    .Flex(grow: 1, basis: 0),
                Button("Import", () => { })
                    .IsEnabled(!isImportRunning)
                    .Set(button => button.Flyout = CreateBrowserImportFlyout(
                        favoritesImportBrowserProfiles,
                        onImportFavorites,
                        onImportBrowserFavorites,
                        onImportBrowserFavoritesProfile,
                        isImportRunning)),
                Button("Delete all favorites", onDeleteAllFavorites)
                    .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                    .AutomationName("Delete all favorites"))
                .HAlign(HorizontalAlignment.Stretch));

        var body = isImportRunning
            ? BuildCommandCenterLoadingState(
                "Gathering favorite items...",
                BuildCommandCenterLoadingRows(4).ToArray())
            : favoriteItems.Count == 0
            ? Border(
                TextBlock("No favorites yet. Star a tab or import bookmarks from another browser.")
                    .Opacity(0.7)
                    .TextWrapping(TextWrapping.Wrap)
            )
            .Padding(8, 4)
            : Border(
                VStack(10,
                    VStack(6, favoriteRows.ToArray())
                        .HAlign(HorizontalAlignment.Stretch),
                    BuildFavoritesPagingFooter(favoriteItems.Count, favoritesLimit, onLoadMoreFavorites))
                    .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(8, 4)
            .HAlign(HorizontalAlignment.Stretch)
            .MinWidth(0);

        return FlexColumn(
            header,
            string.IsNullOrWhiteSpace(favoritesImportStatus)
                ? Border(null).IsVisible(false)
                : Border(
                    TextBlock(favoritesImportStatus)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
            ScrollViewer(body)
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                })
                .VAlign(VerticalAlignment.Stretch)
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0)) with
        {
            RowGap = 12
        };
    }

    private static Element BuildFavoritesPagingFooter(
        int loadedCount,
        int favoritesLimit,
        Action onLoadMoreFavorites)
    {
        var canLoadMore = loadedCount >= favoritesLimit && favoritesLimit < 2500;

        return Border(
            HStack(10,
                TextBlock($"Loaded {loadedCount:n0} favorites")
                    .Opacity(0.72)
                    .VAlign(VerticalAlignment.Center)
                    .Flex(grow: 1, basis: 0),
                Button("Load more", onLoadMoreFavorites)
                    .IsEnabled(canLoadMore)
                    .AutomationName("Load more favorites")
                    .ToolTip(canLoadMore ? "Load more favorites" : "No more favorites loaded")))
            .Padding(8, 6)
            .CornerRadius(10)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element BuildCollectionsBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<BrowserTab> activeTabs,
        IReadOnlyList<TabCollection> collections,
        IReadOnlyList<TabCollectionItem> collectionItems,
        string collectionName,
        string collectionStatus,
        Action<string> onCollectionNameChanged,
        Action onCreateCollection,
        Action onCreateSmartCollections,
        Action<string?> onRefreshCollections,
        Action onDeleteCollection,
        Action onAddCurrentTabToCollection,
        Action<string, string, string> onAddUrlToCollection,
        Action onSetStartupCollection,
        Action<string> onOpenCollectionItem,
        Action<string> onOpenCollectionItemInNewTab,
        Action<string> onRemoveCollectionItem,
        Action<string, int> onMoveCollectionItem,
        bool isCommandCenterExpanded)
    {
        settingsSnapshot.TryGetValue(TabCollectionService.StartupCollectionSettingKey, out var startupCollectionId);
        var selectedCollection = collections.FirstOrDefault(collection =>
            string.Equals(collection.Name, collectionName, StringComparison.OrdinalIgnoreCase));

        async void RemoveCollectionFromPill(TabCollection collection)
        {
            var itemCount = TabCollectionService.GetItems(collection.Id).Count;
            var itemLabel = itemCount == 1 ? "saved page" : "saved pages";
            if (!await ConfirmCollectionContextActionAsync(
                    $"Remove '{collection.Name}'?",
                    $"This collection contains {itemCount} {itemLabel}. Its open tabs, history, and favorites will remain.",
                    "Continue"))
            {
                return;
            }

            if (!await ConfirmCollectionContextActionAsync(
                    "Permanently remove collection?",
                    $"Remove '{collection.Name}', its {itemCount} {itemLabel}, and its LinkScape Desktop launcher? This cannot be undone.",
                    "Remove permanently"))
            {
                return;
            }

            if (!TabCollectionService.DeleteCollection(collection.Id))
            {
                BrowserNoticeService.Show($"Could not find collection '{collection.Name}'.");
                return;
            }

            try
            {
                CollectionShortcutService.Remove(collection.Id);
            }
            catch
            {
                // The collection remains deleted if Windows cannot remove its optional launcher.
            }

            _ = AppJumpListService.RefreshAsync();
            var nextCollectionName = string.Equals(collection.Name, collectionName, StringComparison.OrdinalIgnoreCase)
                ? collections.FirstOrDefault(candidate => !string.Equals(candidate.Id, collection.Id, StringComparison.Ordinal))?.Name ?? "Personal"
                : collectionName;
            BrowserNoticeService.Show($"Removed collection '{collection.Name}'.");
            onRefreshCollections(nextCollectionName);
        }

        var collectionButtons = collections
            .Select(collection =>
            {
                var isStartup = string.Equals(collection.Id, startupCollectionId, StringComparison.Ordinal);
                var isSelected = string.Equals(collection.Name, collectionName, StringComparison.OrdinalIgnoreCase);
                return BuildCollectionSelectorButton(
                    collection,
                    isSelected,
                    isStartup,
                    onCollectionNameChanged,
                    () =>
                    {
                        if (!ActivationRoutingService.RequestCollectionActivation(collection.Id))
                        {
                            BrowserNoticeService.Show($"Could not switch to collection '{collection.Name}'.");
                        }
                    },
                    () => RemoveCollectionFromPill(collection));
            })
            .Cast<Element>()
            .ToArray();

        var itemRows = collectionItems
            .Select((item, index) => BuildCollectionItem(item, index, collectionItems.Count, onOpenCollectionItem, onOpenCollectionItemInNewTab, onRemoveCollectionItem, onMoveCollectionItem))
            .ToArray();

        void SwitchToSelectedCollection()
        {
            if (selectedCollection is not null &&
                !ActivationRoutingService.RequestCollectionActivation(selectedCollection.Id))
            {
                BrowserNoticeService.Show("Could not switch to the selected collection.");
            }
        }

        void OpenSelectedCollectionInNewWindow()
        {
            if (selectedCollection is not null &&
                !ActivationRoutingService.OpenCollectionInNewWindow(selectedCollection.Id))
            {
                BrowserNoticeService.Show("Could not open the collection in a new window.");
            }
        }

        void CreateSelectedCollectionShortcut()
        {
            if (selectedCollection is null)
            {
                BrowserNoticeService.Show("Select a collection before creating a shortcut.");
                return;
            }

            if (TabCollectionService.GetItems(selectedCollection.Id).Count == 0)
            {
                BrowserNoticeService.Show("Add at least one page before creating a collection shortcut.");
                return;
            }

            try
            {
                var shortcut = CollectionShortcutService.CreateOrUpdate(selectedCollection);
                BrowserNoticeService.Show($"Desktop launcher ready: {Path.GetFileName(shortcut.ShortcutPath)}");
                onCollectionNameChanged(selectedCollection.Name);
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not create the desktop launcher: {ex.Message}");
            }
        }

        void RemoveSelectedCollectionShortcut()
        {
            if (selectedCollection is null)
            {
                return;
            }

            try
            {
                var removed = CollectionShortcutService.Remove(selectedCollection.Id);
                BrowserNoticeService.Show(removed
                    ? $"Removed the desktop launcher for {selectedCollection.Name}."
                    : $"No desktop launcher was found for {selectedCollection.Name}.");
                onCollectionNameChanged(selectedCollection.Name);
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not remove the desktop launcher: {ex.Message}");
            }
        }

        ButtonElement ShortcutButton(string glyph, string label, Action action) =>
            Button(
                HStack(7,
                    TextBlock(glyph)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .FontSize(14),
                    TextBlock(label)),
                action)
            .AutomationName(label);

        void RemoveCollectionShortcut(CollectionShortcutInfo shortcut)
        {
            try
            {
                var removed = CollectionShortcutService.Remove(shortcut.CollectionId);
                BrowserNoticeService.Show(removed
                    ? $"Removed the desktop launcher for {shortcut.CollectionName}."
                    : $"No desktop launcher was found for {shortcut.CollectionName}.");
                onCollectionNameChanged(collectionName);
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not remove the desktop launcher: {ex.Message}");
            }
        }

        var selectedShortcut = selectedCollection is null
            ? null
            : CollectionShortcutService.GetStatus(selectedCollection);
        var activeCollectionPageCount = collectionItems.Count(item =>
            activeTabs.Any(tab => string.Equals(tab.Url, item.Url, StringComparison.OrdinalIgnoreCase)));
        var isSelectedCollectionRunning = collectionItems.Count > 0 &&
            activeCollectionPageCount == collectionItems.Count;
        var installedShortcuts = CollectionShortcutService.GetInstalledShortcuts(collections);

        var shortcutRows = installedShortcuts
            .Select(shortcut =>
                HStack(8,
                    TextBlock(shortcut.IsValid ? BrowserConstants.GlyphCheckMark : BrowserConstants.GlyphWarning)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .Foreground(shortcut.IsValid
                            ? BrowserMaterialTheme.SelectedStrokeBrush
                            : BrowserConstants.AccentFillColorDefaultBrush),
                    VStack(1,
                        TextBlock($"Start {shortcut.CollectionName}")
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock(shortcut.IsValid ? "Ready on Desktop" : "Needs repair")
                            .FontSize(12)
                            .Opacity(0.68))
                    .Flex(grow: 1, basis: 0),
                    IconButton(
                        BrowserConstants.GlyphTrash,
                        () => RemoveCollectionShortcut(shortcut),
                        $"Remove desktop launcher for {shortcut.CollectionName}"))
                .Padding(8, 5)
                .CornerRadius(6)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                .HAlign(HorizontalAlignment.Stretch))
            .Cast<Element>()
            .ToArray();

        var shortcutActions = isCommandCenterExpanded
            ? FlexRow(
                ShortcutButton(
                    selectedShortcut?.Exists == true ? BrowserConstants.GlyphRefresh : BrowserConstants.GlyphLink,
                    selectedShortcut?.Exists == true ? "Repair shortcut" : "Create shortcut",
                    CreateSelectedCollectionShortcut)
                    .Flex(grow: 1, basis: 150)
                    .IsEnabled(selectedCollection is not null),
                ShortcutButton(BrowserConstants.GlyphTrash, "Remove", RemoveSelectedCollectionShortcut)
                    .Flex(grow: 1, basis: 96)
                    .IsEnabled(selectedShortcut?.Exists == true)) with
            {
                ColumnGap = 8,
                RowGap = 8,
                Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
            }
            : FlexRow(
                IconButton(
                    selectedShortcut?.Exists == true ? BrowserConstants.GlyphRefresh : BrowserConstants.GlyphLink,
                    CreateSelectedCollectionShortcut,
                    selectedShortcut?.Exists == true ? "Repair desktop shortcut" : "Create desktop shortcut")
                    .IsEnabled(selectedCollection is not null),
                IconButton(BrowserConstants.GlyphTrash, RemoveSelectedCollectionShortcut, "Remove desktop shortcut")
                    .IsEnabled(selectedShortcut?.Exists == true)) with
            {
                ColumnGap = 8,
                RowGap = 8,
                Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
            };

        var shortcutsPanel = VStack(8,
            HStack(8,
                TextBlock(BrowserConstants.GlyphLink)
                    .FontFamily(BrowserConstants.IconFontFamily)
                    .FontSize(16),
                VStack(1,
                    TextBlock("Desktop launchers")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(isSelectedCollectionRunning
                            ? $"Shortcut opens all {collectionItems.Count} saved pages | collection is active"
                            : activeCollectionPageCount > 0
                                ? $"Shortcut opens all {collectionItems.Count} saved pages | {activeCollectionPageCount} already open"
                                : installedShortcuts.Count == 0
                                    ? $"Create a Desktop shortcut that opens all {collectionItems.Count} saved pages."
                                    : $"Opens all {collectionItems.Count} saved pages | {installedShortcuts.Count} installed")
                        .FontSize(12)
                        .Opacity(0.68))
                    .Flex(grow: 1, basis: 0)),
            shortcutActions,
            shortcutRows.Length == 0
                ? Border(null).IsVisible(false)
                : VStack(4, shortcutRows).HAlign(HorizontalAlignment.Stretch))
            .Padding(14)
            .CornerRadius(8)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);

        string pendingUrl = string.Empty;
        string pendingTitle = string.Empty;
        Microsoft.UI.Xaml.Controls.AutoSuggestBox? urlInput = null;
        Microsoft.UI.Xaml.Controls.AutoSuggestBox? titleInput = null;

        void AddEnteredUrl()
        {
            var url = pendingUrl.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                BrowserNoticeService.Show("Enter a complete http:// or https:// address.");
                urlInput?.Focus(FocusState.Programmatic);
                return;
            }

            var targetCollectionName = selectedCollection?.Name ?? collectionName;
            onAddUrlToCollection(targetCollectionName, parsedUri.AbsoluteUri, pendingTitle.Trim());
            pendingUrl = string.Empty;
            pendingTitle = string.Empty;
            if (urlInput is not null)
            {
                urlInput.Text = string.Empty;
            }
            if (titleInput is not null)
            {
                titleInput.Text = string.Empty;
            }
        }

        var addUrlPanel = VStack(8,
            HStack(8,
                TextBlock(BrowserConstants.GlyphLink)
                    .FontFamily(BrowserConstants.IconFontFamily)
                    .FontSize(16),
                VStack(1,
                    TextBlock("Add a page")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(selectedCollection is null
                            ? "Create or select a collection first."
                            : $"Save a URL directly to {selectedCollection.Name}.")
                        .FontSize(12)
                        .Opacity(0.68))
                    .Flex(grow: 1, basis: 0)),
            AutoSuggestBox(string.Empty, value => pendingUrl = value, submitted =>
                {
                    pendingUrl = submitted;
                    AddEnteredUrl();
                })
                .Set(input => urlInput = input)
                .AutomationName("Page URL")
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0) with
            {
                PlaceholderText = "https://example.com"
            },
            (FlexRow(
                AutoSuggestBox(string.Empty, value => pendingTitle = value, submitted =>
                    {
                        pendingTitle = submitted;
                        AddEnteredUrl();
                    })
                    .Set(input => titleInput = input)
                    .AutomationName("Page title")
                    .HAlign(HorizontalAlignment.Stretch)
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 190) with
                {
                    PlaceholderText = "Title (optional)"
                },
                ShortcutButton(BrowserConstants.GlyphAdd, "Add URL", AddEnteredUrl)
                    .IsEnabled(selectedCollection is not null)
                    .Flex(grow: 1, basis: 104)) with
            {
                ColumnGap = 8,
                RowGap = 8,
                Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
            }))
            .Padding(14)
            .CornerRadius(8)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);

        var collectionActions = isCommandCenterExpanded
            ? FlexRow(
                ShortcutButton(BrowserConstants.GlyphAdd, "Add current tab", onAddCurrentTabToCollection)
                    .Flex(grow: 1, basis: 140),
                ShortcutButton(BrowserConstants.GlyphFavoriteOutline, "Use at startup", onSetStartupCollection)
                    .Flex(grow: 1, basis: 130),
                ShortcutButton(BrowserConstants.GlyphGo, "Switch to", SwitchToSelectedCollection)
                    .Flex(grow: 1, basis: 100)
                    .IsEnabled(selectedCollection is not null),
                ShortcutButton(BrowserConstants.GlyphNewWindow, "New window", OpenSelectedCollectionInNewWindow)
                    .Flex(grow: 1, basis: 110)
                    .IsEnabled(selectedCollection is not null),
                ShortcutButton(BrowserConstants.GlyphTrash, "Delete", onDeleteCollection)
                    .Flex(grow: 1, basis: 88)
                    .IsEnabled(selectedCollection is not null)) with
            {
                ColumnGap = 8,
                RowGap = 8,
                Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
            }
            : FlexRow(
                IconButton(BrowserConstants.GlyphAdd, onAddCurrentTabToCollection, "Add current tab to collection"),
                IconButton(BrowserConstants.GlyphFavoriteOutline, onSetStartupCollection, "Set startup collection"),
                IconButton(BrowserConstants.GlyphGo, SwitchToSelectedCollection, "Switch to selected collection")
                    .IsEnabled(selectedCollection is not null),
                IconButton(BrowserConstants.GlyphNewWindow, OpenSelectedCollectionInNewWindow, "Open selected collection in a new window")
                    .IsEnabled(selectedCollection is not null),
                IconButton(BrowserConstants.GlyphTrash, onDeleteCollection, "Delete selected collection")
                    .IsEnabled(selectedCollection is not null)) with
            {
                ColumnGap = 8,
                RowGap = 8,
                Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
            };

        var collectionRunState = isSelectedCollectionRunning
            ? $"Running | {activeCollectionPageCount} pages active"
            : activeCollectionPageCount > 0
                ? $"Partial | {activeCollectionPageCount} of {collectionItems.Count} pages active"
                : "Ready";
        var collectionActionsPanel = VStack(10,
                HStack(8,
                    TextBlock(BrowserConstants.GlyphSettings)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .FontSize(16),
                    VStack(1,
                        TextBlock(selectedCollection?.Name ?? "Selected collection")
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock(collectionRunState)
                            .FontSize(12)
                            .Opacity(0.68))
                    .Flex(grow: 1, basis: 0)),
                collectionActions)
            .Padding(14)
            .CornerRadius(8)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);
        var smartCollectionsPanel = VStack(10,
                HStack(8,
                    TextBlock(BrowserConstants.GlyphCollections)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .FontSize(16),
                    VStack(1,
                        TextBlock("Smart Collections")
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock("Creates topic collections from local History and Favorites. No AI is used.")
                            .FontSize(12)
                            .Opacity(0.68)
                            .TextWrapping(TextWrapping.Wrap))
                    .Flex(grow: 1, basis: 0)),
                isCommandCenterExpanded
                    ? ShortcutButton(BrowserConstants.GlyphRefresh, "Create / refresh", onCreateSmartCollections)
                        .HAlign(HorizontalAlignment.Stretch)
                    : IconButton(BrowserConstants.GlyphRefresh, onCreateSmartCollections, "Create or refresh Smart Collections"))
            .Padding(14)
            .CornerRadius(8)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .HAlign(HorizontalAlignment.Stretch);
        var collectionTools = Expander(
                "Collection tools",
                VStack(20,
                    smartCollectionsPanel,
                    collectionActionsPanel,
                    shortcutsPanel,
                    addUrlPanel)
                .Padding(12, 10, 12, 14)
                .HAlign(HorizontalAlignment.Stretch))
            .HeaderTemplate(
                HStack(10,
                    TextBlock(BrowserConstants.GlyphSettings)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .FontSize(16),
                    VStack(1,
                        TextBlock("Collection tools")
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                        TextBlock(collectionRunState)
                            .FontSize(12)
                            .Opacity(0.68))
                    .Flex(grow: 1, basis: 0)))
            .HAlign(HorizontalAlignment.Stretch);

        var collectionSelectorPanel = collectionButtons.Length == 0
            ? Border(
                TextBlock("No collections yet. Create one to begin saving pages.")
                    .Opacity(0.72)
                    .TextWrapping(TextWrapping.Wrap))
                .Padding(12, 10)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            : Border(
                (FlexRow(collectionButtons) with
                {
                    ColumnGap = 8,
                    RowGap = 8,
                    Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
                })
                .HAlign(HorizontalAlignment.Stretch))
                .Padding(10)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                .HAlign(HorizontalAlignment.Stretch);

        var header = VStack(10,
            TextBlock("Collections")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            collectionSelectorPanel,
            Expander(
                    "New collection",
                    (FlexRow(
                        AutoSuggestBox(collectionName, onCollectionNameChanged, submitted => onCollectionNameChanged(submitted))
                            .AutomationName("New collection name")
                            .HAlign(HorizontalAlignment.Stretch)
                            .MinWidth(0)
                            .Flex(grow: 1, basis: 190) with
                        {
                            PlaceholderText = "Collection name"
                        },
                        ShortcutButton(BrowserConstants.GlyphAdd, "Create", onCreateCollection)
                            .Flex(grow: 1, basis: 96)) with
                    {
                        ColumnGap = 8,
                        RowGap = 8,
                        Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
                    })
                    .Padding(12, 8, 12, 12)
                    .HAlign(HorizontalAlignment.Stretch))
                .HeaderTemplate(
                    HStack(8,
                        TextBlock(BrowserConstants.GlyphAdd)
                            .FontFamily(BrowserConstants.IconFontFamily)
                            .FontSize(15),
                        TextBlock("New collection")
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)))
                .HAlign(HorizontalAlignment.Stretch));

        var body = VStack(12,
            collectionTools,
            itemRows.Length == 0
                ? Border(
                    TextBlock("No items in this collection yet.")
                        .Opacity(0.7))
                    .Padding(8, 4)
                : Border(
                    VStack(6, itemRows)
                        .HAlign(HorizontalAlignment.Stretch))
                    .Padding(4, 0)
                    .HAlign(HorizontalAlignment.Stretch)
                    .MinWidth(0));

        return FlexColumn(
            header,
            ScrollViewer(body)
                .Set(scrollViewer =>
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                })
                .VAlign(VerticalAlignment.Stretch)
                .MinHeight(0)
                .Flex(grow: 1, shrink: 1, basis: 0)) with
        {
            RowGap = 12
        };
    }

    private static ButtonElement BuildCollectionSelectorButton(
        TabCollection collection,
        bool isSelected,
        bool isStartup,
        Action<string> onCollectionNameChanged,
        Action onSwitchToCollection,
        Action onRemoveCollection)
    {
        var collectionName = collection.Name;
        Microsoft.UI.Xaml.Controls.Border? underline = null;
        var content = VStack(3,
            HStack(6,
                TextBlock(collectionName)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .TextWrapping(TextWrapping.NoWrap)
                    .Set(textBlock =>
                    {
                        textBlock.MaxLines = 1;
                        textBlock.FontWeight = isSelected
                            ? Microsoft.UI.Text.FontWeights.SemiBold
                            : Microsoft.UI.Text.FontWeights.Normal;
                    }),
                TextBlock(isSelected ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline)
                    .FontFamily(BrowserConstants.IconFontFamily)
                    .FontSize(12)
                    .Foreground(BrowserMaterialTheme.SelectedStrokeBrush)
                    .ToolTip("Startup collection")
                    .IsVisible(isStartup))
                .VAlign(VerticalAlignment.Center),
            Border(null)
                .Height(2)
                .CornerRadius(1)
                .Background(BrowserMaterialTheme.SelectedStrokeBrush)
                .Opacity(isSelected ? 1 : 0)
                .HAlign(HorizontalAlignment.Stretch)
                .Set(border => underline = border));

        return Button(content, () => onCollectionNameChanged(collectionName))
            .AutomationName($"Open collection {collectionName}")
            .Padding(12, 5)
            .CornerRadius(6)
            .Background(isSelected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush)
            .WithBorder(isSelected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush)
            .Set(button =>
            {
                ConfigureCollectionSelectorButton(button, underline, isSelected);
                button.ContextFlyout = CreateCollectionSelectorContextFlyout(
                    collectionName,
                    onSwitchToCollection,
                    onRemoveCollection);
            });
    }

    private static MenuFlyout CreateCollectionSelectorContextFlyout(
        string collectionName,
        Action onSwitchToCollection,
        Action onRemoveCollection)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateOverflowMenuItem(
            $"Switch to {collectionName}",
            BrowserConstants.GlyphGo,
            onSwitchToCollection));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateOverflowMenuItem(
            $"Remove {collectionName}...",
            BrowserConstants.GlyphTrash,
            onRemoveCollection));
        return flyout;
    }

    private static async Task<bool> ConfirmCollectionContextActionAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        var xamlRoot = global::LinkScape.Application.MainWindowActivation.GetXamlRoot();
        if (xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static void ConfigureCollectionSelectorButton(
        Microsoft.UI.Xaml.Controls.Button button,
        Microsoft.UI.Xaml.Controls.Border? underline,
        bool isSelected)
    {
        button.BorderThickness = new Thickness(1);
        button.Tag = new CollectionSelectorHoverState(underline, isSelected);
        button.PointerEntered -= OnCollectionSelectorPointerEntered;
        button.PointerEntered += OnCollectionSelectorPointerEntered;
        button.PointerExited -= OnCollectionSelectorPointerExited;
        button.PointerExited += OnCollectionSelectorPointerExited;
        button.PointerCanceled -= OnCollectionSelectorPointerExited;
        button.PointerCanceled += OnCollectionSelectorPointerExited;
        button.PointerCaptureLost -= OnCollectionSelectorPointerExited;
        button.PointerCaptureLost += OnCollectionSelectorPointerExited;
    }

    private sealed record CollectionSelectorHoverState(
        Microsoft.UI.Xaml.Controls.Border? Underline,
        bool IsSelected);

    private static void OnCollectionSelectorPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: CollectionSelectorHoverState { Underline: { } underline } })
        {
            underline.Opacity = 1;
        }
    }

    private static void OnCollectionSelectorPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: CollectionSelectorHoverState { Underline: { } underline } state })
        {
            underline.Opacity = state.IsSelected ? 1 : 0;
        }
    }

    private static Element BuildCollectionItem(
        TabCollectionItem item,
        int itemIndex,
        int itemCount,
        Action<string> onOpenCollectionItem,
        Action<string> onOpenCollectionItemInNewTab,
        Action<string> onRemoveCollectionItem,
        Action<string, int> onMoveCollectionItem)
    {
        return Border(
            (FlexRow(
                Button(
                    (FlexRow(
                        BuildHistoryIcon(item.Url),
                        VStack(2,
                            TextBlock(item.Title)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                }),
                            TextBlock(item.Url)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Opacity(0.75)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                })
                        )
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0)
                    ) with
                    {
                        ColumnGap = 8
                    })
                    .HAlign(HorizontalAlignment.Stretch),
                    () => onOpenCollectionItem(item.Url))
                    .Padding(0)
                    .Background(new SolidColorBrush(Microsoft.UI.Colors.Transparent))
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
                    .Set(button =>
                    {
                        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title);
                        button.ContextFlyout = CreateOpenItemContextFlyout(
                            item.Url,
                            onOpenCollectionItem,
                            onOpenCollectionItemInNewTab,
                            () => onRemoveCollectionItem(item.Url),
                            "Remove from collection",
                            "↕️ Move",
                            itemIndex,
                            itemCount,
                            targetIndex => onMoveCollectionItem(item.Id, targetIndex));
                    }),
                IconButton(BrowserConstants.GlyphClose, () => onRemoveCollectionItem(item.Url), "Remove from collection", buttonSize: 24, iconSize: 10, useGlass: true)
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 8
            })
            .HAlign(HorizontalAlignment.Stretch))
        .Padding(8, 6)
        .CornerRadius(8)
        .Margin(2, 0)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName("CollectionItem");
    }

    private static Element BuildCommandCenterLoadingState(string message, params Element[] placeholders)
    {
        return Border(
            VStack(12,
                HStack(8,
                    ProgressRing()
                        .Width(16)
                        .Height(16)
                        .IsActive(true),
                    TextBlock(message)
                        .Opacity(0.82)
                        .TextWrapping(TextWrapping.Wrap)
                ),
                placeholders.Length == 0
                    ? Border(null).IsVisible(false)
                    : VStack(8, placeholders)
                        .HAlign(HorizontalAlignment.Stretch)
            )
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(12)
        .CornerRadius(12)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .WithBorder(Theme.SurfaceStroke)
        .HAlign(HorizontalAlignment.Stretch);
    }

    private static IEnumerable<Element> BuildCommandCenterLoadingRows(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return Border(
                HStack(10,
                    Border(null)
                        .Width(24)
                        .Height(24)
                        .CornerRadius(6)
                        .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
                    VStack(6,
                        Border(null)
                            .Width(index % 2 == 0 ? 196 : 164)
                            .Height(10)
                            .CornerRadius(999)
                            .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
                        Border(null)
                            .Width(index % 2 == 0 ? 138 : 116)
                            .Height(8)
                            .CornerRadius(999)
                            .Background(BrowserConstants.LayerFillAltBrush)
                    )
                    .Flex(grow: 1, basis: 0)
                )
                .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(10, 8)
            .CornerRadius(10)
            .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
            .WithBorder(Theme.SurfaceStroke)
            .HAlign(HorizontalAlignment.Stretch);
        }
    }

    private static IEnumerable<Element> BuildCommandCenterLoadingGrid(int cardCount)
    {
        var cards = new List<Element>();

        for (var index = 0; index < cardCount; index++)
        {
            cards.Add(
                Border(
                    VStack(8,
                        Border(null)
                            .Width(28)
                            .Height(28)
                            .CornerRadius(8)
                            .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
                        Border(null)
                            .Width(index % 2 == 0 ? 126 : 112)
                            .Height(10)
                            .CornerRadius(999)
                            .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
                        Border(null)
                            .Width(index % 2 == 0 ? 98 : 86)
                            .Height(8)
                            .CornerRadius(999)
                            .Background(BrowserConstants.LayerFillAltBrush)
                    )
                )
                .Padding(12)
                .CornerRadius(12)
                .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
                .WithBorder(Theme.SurfaceStroke)
                .Flex(grow: 1, basis: 0));
        }

        for (var index = 0; index < cards.Count; index += 2)
        {
            yield return HStack(8,
                cards[index],
                cards[index + 1]);
        }
    }

    private static Element BuildSettingsBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue)
    {
        var homeUrl = settingsSnapshot.TryGetValue(BrowserConstants.HomeUrlSettingKey, out var configuredHomeUrl)
            ? BrowserUrl.Normalize(configuredHomeUrl, BrowserConstants.HomeUrl)
            : BrowserConstants.HomeUrl;
        var settingsItems = settingsSnapshot
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new SettingGridItem
            {
                Key = entry.Key,
                Value = entry.Value
            })
            .ToList();
        var saveTabs = GetBooleanSetting(settingsSnapshot, BrowserConstants.SaveTabsSettingKey, true);
        var historyOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var favoritesOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.FavoritesOpenInNewTabSettingKey);
        var addressBarOpenDifferentDomainInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey);
        var automaticDailyUpdateChecks = GetBooleanSetting(settingsSnapshot, AppUpdateService.AutomaticDailyChecksSettingKey, true);

        return VStack(10,
            TextBlock("Settings")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            TextBlock("Current values from Documents\\LinkScapeCache\\settings.db.")
                .TextWrapping(TextWrapping.Wrap)
                .Opacity(0.76),
            Border(
                VStack(8,
                    TextBlock("Home page")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(homeUrl)
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(0.76),
                    TextBlock("The Home button, new tabs, and replacing the last closed tab use this URL. Use the title bar button to capture the current page.")
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(0.68),
                    Button("Reset home to default", () => onSaveSettingValue(BrowserConstants.HomeUrlSettingKey, BrowserConstants.HomeUrl))
                        .CornerRadius(999)
                        .Padding(12, 6)
                )
            )
            .Padding(10)
            .WithBorder(Theme.SurfaceStroke),
            Border(
                VStack(10,
                    TextBlock("Open behavior")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    BuildBooleanSettingRow(
                        "Restore tabs from last session",
                        "When enabled, LinkScape saves open tabs and restores them on the next launch. When disabled, startup opens a fresh home page.",
                        saveTabs,
                        nextValue => onSaveSettingValue(BrowserConstants.SaveTabsSettingKey, nextValue ? "true" : "false")),
                    BuildBooleanSettingRow(
                        "History opens in new tab",
                        "History, Recent, and Most visited items open in a new tab by default.",
                        historyOpenInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.HistoryOpenInNewTabSettingKey, nextValue ? "true" : "false")),
                    BuildBooleanSettingRow(
                        "Favorites open in new tab",
                        "Favorite items open in a new tab by default.",
                        favoritesOpenInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.FavoritesOpenInNewTabSettingKey, nextValue ? "true" : "false")),
                    BuildBooleanSettingRow(
                        "Address bar opens different domains in new tab",
                        "When enabled, entering a normalized URL in the address bar opens a new tab if the destination host differs from the current tab.",
                        addressBarOpenDifferentDomainInNewTab,
                        nextValue => onSaveSettingValue(BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey, nextValue ? "true" : "false"))
                )
            )
            .Padding(10)
            .WithBorder(Theme.SurfaceStroke),
            Border(
                VStack(10,
                    TextBlock("LinkScape updates")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock("Keep LinkScape current through Microsoft Store. Update prompts and progress appear beside Settings in the upper-right corner.")
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(0.76),
                    BuildBooleanSettingRow(
                        "Check for updates daily",
                        "When enabled, LinkScape checks once every 24 hours and lets you install now or later.",
                        automaticDailyUpdateChecks,
                        nextValue => onSaveSettingValue(AppUpdateService.AutomaticDailyChecksSettingKey, nextValue ? "true" : "false")),
                    Button("Check for updates now", () => _ = AppUpdateService.CheckForUpdatesNowAsync())
                        .CornerRadius(999)
                        .Padding(12, 6)
                )
            )
            .Padding(10)
            .WithBorder(Theme.SurfaceStroke),
            Border(
                VStack(8,
                    TextBlock("First-time setup")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock("Reopen the setup used to import browser data and choose a search provider.")
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(0.76),
                    Button(
                        "Run setup again",
                        () => onSaveSettingValue(
                            FirstRunExperienceService.SettingKey,
                            FirstRunExperienceService.PendingValue))
                        .CornerRadius(6)
                        .Padding(12, 6)
                )
            )
            .Padding(10)
            .WithBorder(Theme.SurfaceStroke),
            settingsItems.Count == 0
                ? Border(
                    TextBlock("No settings were found.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : ScrollViewer(
                    VStack(8,
                        settingsItems.Select(item =>
                            Border(
                                VStack(2,
                                    TextBlock(item.Key)
                                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                                    TextBlock(string.IsNullOrWhiteSpace(item.Value) ? "(empty)" : item.Value)
                                        .TextWrapping(TextWrapping.Wrap)
                                        .Opacity(0.76)
                                )
                            )
                            .WithKey(item.Key)
                            .Padding(8)
                            .WithBorder(Theme.SurfaceStroke)
                        ).ToArray()
                    )
                )
                .Height(320)
        );
    }

    private static Element BuildBooleanSettingRow(
        string title,
        string description,
        bool value,
        Action<bool> onChanged)
    {
        return Border(
            (FlexRow(
                VStack(2,
                    TextBlock(title)
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(description)
                        .TextWrapping(TextWrapping.Wrap)
                        .Opacity(0.72)
                )
                .MinWidth(0)
                .Flex(grow: 1, basis: 0),
                Button(value ? "On" : "Off", () => onChanged(!value))
                    .Background(value ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush)
                    .WithBorder(value ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush)
                    .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
                    .CornerRadius(10)
                    .Padding(12, 6)
                    .MinWidth(56)
                    .AutomationName("Toggle " + title + " setting")
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 12
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(8)
        .WithBorder(Theme.SurfaceStroke)
        .HAlign(HorizontalAlignment.Stretch)
        .MinWidth(0);
    }

    private static bool GetBooleanSetting(IReadOnlyDictionary<string, string> settingsSnapshot, string key, bool defaultValue = false)
    {
        return settingsSnapshot.TryGetValue(key, out var value) && bool.TryParse(value, out var enabled)
            ? enabled
            : defaultValue;
    }

    private static Element BuildBackdropBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue)
    {
        var selectedBackdropPreset = settingsSnapshot.TryGetValue(BackdropGradientPresetSettingKey, out var configuredPreset)
            ? NormalizeBackdropGradientPreset(configuredPreset)
            : BackdropGradientPresetDefault;
        var selectedMaterialTheme = settingsSnapshot.TryGetValue(BrowserMaterialTheme.SettingKey, out var configuredMaterialTheme)
            ? BrowserMaterialTheme.NormalizePreset(configuredMaterialTheme)
            : BrowserMaterialTheme.DefaultPreset;

        return VStack(10,
            BuildInsetOptionCard(
                "Backdrop tint",
                "Choose an optional color wash over the app material.",
                BuildBackdropPresetPicker(selectedBackdropPreset, onSaveSettingValue)),
            BuildInsetOptionCard(
                "Control theme",
                "Mica is balanced with every backdrop. Frost favors Ocean, Graphite, and Aurora; Petal favors Aurora, Sunset, and Forest. Themes repaint controls, pills, badges, borders, and chat messages.",
                BuildMaterialThemePresetPicker(selectedMaterialTheme, onSaveSettingValue))
        );
    }

    private static Element BuildInsetOptionCard(string title, string description, Element content)
    {
        return Border(
            VStack(8,
                TextBlock(title)
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                TextBlock(description)
                    .TextWrapping(TextWrapping.Wrap)
                    .Opacity(0.76),
                content)
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(10)
        .CornerRadius(12)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush)
        .HAlign(HorizontalAlignment.Stretch);
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

    private static Element BuildBackdropPresetPicker(
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var presetButtons = new Element[]
        {
            BuildBackdropPresetButton(BackdropGradientPresetDefault, selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("Aurora", selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("Sunset", selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("Ocean", selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("Graphite", selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("Forest", selectedPreset, onSaveSettingValue),
            BuildBackdropPresetButton("HighContrast", selectedPreset, onSaveSettingValue)
        };

        return FlexRow(presetButtons) with
        {
            ColumnGap = 8,
            RowGap = 8,
            Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
        };
    }

    private static Element BuildBackdropPresetButton(
        string preset,
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var normalizedPreset = NormalizeBackdropGradientPreset(preset);
        var isSelected = string.Equals(selectedPreset, normalizedPreset, StringComparison.Ordinal);

        var displayName = string.Equals(normalizedPreset, "HighContrast", StringComparison.Ordinal)
            ? "High contrast"
            : normalizedPreset;

        return Button(displayName, () => onSaveSettingValue(BackdropGradientPresetSettingKey, normalizedPreset))
            .Background(isSelected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush)
            .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
            .WithBorder(isSelected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush)
            .CornerRadius(6)
            .Padding(13, 6)
            .AutomationName("Select backdrop gradient preset: " + normalizedPreset)
            .MinWidth(72);
    }

    private static Element BuildMaterialThemePresetPicker(
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var presetButtons = BrowserMaterialTheme.Presets
            .Select(preset => BuildMaterialThemePresetButton(preset, selectedPreset, onSaveSettingValue))
            .ToArray();

        return FlexRow(presetButtons) with
        {
            ColumnGap = 8,
            RowGap = 8,
            Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap
        };
    }

    private static Element BuildMaterialThemePresetButton(
        string preset,
        string selectedPreset,
        Action<string, string> onSaveSettingValue)
    {
        var normalizedPreset = BrowserMaterialTheme.NormalizePreset(preset);
        var isSelected = string.Equals(selectedPreset, normalizedPreset, StringComparison.Ordinal);

        return Button(GetMaterialThemeDisplayName(normalizedPreset), () => onSaveSettingValue(BrowserMaterialTheme.SettingKey, normalizedPreset))
            .Background(isSelected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush)
            .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
            .WithBorder(isSelected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush)
            .CornerRadius(6)
            .Padding(13, 6)
            .AutomationName("Select material theme: " + normalizedPreset)
            .MinWidth(string.Equals(normalizedPreset, BrowserMaterialTheme.HighContrastPreset, StringComparison.Ordinal) ? 114 : 82);
    }

    private static string GetMaterialThemeDisplayName(string preset)
    {
        return preset switch
        {
            BrowserMaterialTheme.DefaultThemePreset => "Mica",
            BrowserMaterialTheme.FluentPreset => "Frost",
            BrowserMaterialTheme.MaterialPreset => "Petal",
            BrowserMaterialTheme.HighContrastPreset => "High contrast",
            _ => preset
        };
    }
    
    
    private static Element BuildLocalToolCard(string name, string description)
    {
        return Border(
            VStack(4,
                TextBlock(name)
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                TextBlock(description)
                    .TextWrapping(TextWrapping.Wrap)
                    .Opacity(0.82)))
            .Padding(10)
            .CornerRadius(10)
            .Background(BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(Theme.SurfaceStroke);
    }

    private static Element BuildPlaceholderBladeContent(string title, string message)
    {
        return VStack(8,
            TextBlock(title)
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            TextBlock(message)
                .TextWrapping(TextWrapping.Wrap));
    }

    private static Style GetTabItemContainerStyle(bool isTabsCollapsed)
    {
        if (isTabsCollapsed)
        {
            return _collapsedTabItemContainerStyle ??= CreateTabItemContainerStyle(true);
        }

        return _expandedTabItemContainerStyle ??= CreateTabItemContainerStyle(false);
    }
    //private static Style CreateTabItemContainerStyle(bool isTabsCollapsed)
    //{
    //    var style = new Style(typeof(ListViewItem));

    //    style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
    //    style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));

    //    if (!isTabsCollapsed)
    //    {
    //        style.Setters.Add(
    //            new Setter(
    //                FrameworkElement.MinWidthProperty,
    //                280d));
    //    }

    //    style.Setters.Add(
    //        new Setter(
    //            Control.HorizontalContentAlignmentProperty,
    //            isTabsCollapsed
    //                ? HorizontalAlignment.Center
    //                : HorizontalAlignment.Stretch));

    //    return style;
    //}

    private static Style CreateTabItemContainerStyle(bool isTabsCollapsed)
    {
        var style = new Style(typeof(ListViewItem));
        var itemMargin = isTabsCollapsed
            ? new Thickness(TabItemHorizontalInset, 8, TabItemHorizontalInset, 8)
            : new Thickness(TabItemHorizontalInset, 8, TabItemHorizontalInset, 8);

        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, itemMargin));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, isTabsCollapsed ? 0d : 280d));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, isTabsCollapsed ? CollapsedTabItemHeight : double.NaN));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, isTabsCollapsed
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        
        //style.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty, new Windows.Foundation.Point(0.5, 0.5)));
        //style.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform { ScaleX = 1, ScaleY = 1 }));

        return style;
    }

    private static void OnTabListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem container)
        {
            ConfigureTabItemHoverScale(container);
        }
    }

    private static void ConfigureTabItemHoverScale(ListViewItem container)
    {
        if (container.RenderTransform is not ScaleTransform)
        {
            container.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            container.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        }

        container.PointerEntered -= OnTabItemPointerEntered;
        container.PointerEntered += OnTabItemPointerEntered;
        container.PointerExited -= OnTabItemPointerExited;
        container.PointerExited += OnTabItemPointerExited;
        container.PointerCanceled -= OnTabItemPointerExited;
        container.PointerCanceled += OnTabItemPointerExited;
        container.PointerCaptureLost -= OnTabItemPointerExited;
        container.PointerCaptureLost += OnTabItemPointerExited;
    }

    private static void OnTabItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetTabItemScale(sender, TabItemHoverScale);
    }

    private static void OnTabItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetTabItemScale(sender, 1d);
    }

    private static void SetTabItemScale(object sender, double scale)
    {
        if (sender is ListViewItem { RenderTransform: ScaleTransform transform })
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
    }

    private static Element BuildCollapsedTabItem(
        BrowserTab tab,
        bool isSelected,
        bool isLoading,
        int tabIndex,
        int tabCount,
        IReadOnlyList<TabCollection> tabCollections,
        Action<string, string, string> onAddUrlToCollection,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab,
        Action<string> onOpenTabInNewWindow,
        Action<string, int> onMoveTab,
        Func<BrowserTab, string?> getTabInstalledWebAppName,
        Func<BrowserTab, string?> getTabInstallableWebAppName,
        Action<string> onOpenTabAsWebApp,
        Action<string> onInstallTabWebApp)
    {
        return Border(
            Border(
                BuildTabIcon(tab, isLoading, useTileChrome: false).WithKey("CollapsedTabIcon" + tab.Id)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
            .Width(26)
            .Height(26)
            .CornerRadius(9)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x10, 0xFF, 0xFF, 0xFF)))
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
        )
        .Width(CollapsedTabItemHeight)
        .Height(CollapsedTabItemHeight)
        .Padding()
        .Set(border => border.Style = GetCollapsedTabGlassCardStyle())
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, tabCollections, onAddUrlToCollection, onToggleFavoriteTab, onCloseTab, onReloadTab, onOpenTabInNewWindow, onMoveTab, tabIndex, tabCount, getTabInstalledWebAppName, getTabInstallableWebAppName, onOpenTabAsWebApp, onInstallTabWebApp);
            ToolTipService.SetToolTip(border, CreateTabToolTip(tab));
            ApplyTabItemBorderState(border, isSelected, IsTabCreationLoading(tab, isLoading));
        });
    }



    private static Element BuildExpandedTabItem(
        BrowserTab tab,
        bool isSelected,
        bool isLoading,
        int tabIndex,
        int tabCount,
        IReadOnlyList<string> collectionNames,
        IReadOnlyList<TabCollection> tabCollections,
        Action<string, string, string> onAddUrlToCollection,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab,
        Action<string> onOpenTabInNewWindow,
        Action<string, int> onMoveTab,
        Func<BrowserTab, string?> getTabInstalledWebAppName,
        Func<BrowserTab, string?> getTabInstallableWebAppName,
        Action<string> onOpenTabAsWebApp,
        Action<string> onInstallTabWebApp)
    {
        return Border(
            Grid(
                [GridSize.Star()],
                [GridSize.Star()],
                (FlexRow(
                BuildTabIcon(tab, isLoading),
                Border(
                    VStack(6,
                        TextBlock(tab.Title)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.Wrap)
                            .Set(textBlock =>
                            {
                                textBlock.FontFamily = BrowserConstants.TextFontFamily;
                                textBlock.MaxLines = collectionNames.Count > 0 ? 1 : 2;
                                textBlock.MinHeight = collectionNames.Count > 0 ? 22 : 38;
                                textBlock.MinWidth = 0;
                            }),
                        BuildCollectionBadgeRow(collectionNames, maxBadges: 1))
                )
                .MinWidth(0)
                .Flex(grow: 1, basis: 0),
                Border(
                    (FlexColumn(
                        Border(null)
                            .Flex(grow: 1, basis: 0),
                        IconButton(
                            BrowserConstants.GlyphTrash,
                            () => onCloseTab(tab.Id),
                            "Close tab",
                            buttonSize: 24,
                            iconSize: 10,
                            useGlass: true)
                            .HAlign(HorizontalAlignment.Right)
                            .VAlign(VerticalAlignment.Bottom)
                            .Flex(shrink: 0)
                    ) with
                    {
                        RowGap = 6
                    })
                    .VAlign(VerticalAlignment.Stretch)
                )
                .Width(34)
                .HAlign(HorizontalAlignment.Right)
                .Flex(shrink: 0)
                ) with
                {
                    ColumnGap = 12
                })
                .HAlign(HorizontalAlignment.Stretch)
                .Grid(row: 0, column: 0),
                Border(
                    TextBlock("💤")
                        .FontSize(11)
                        .Foreground(BrowserMaterialTheme.BadgeForegroundBrush)
                        .AutomationName("Sleeping tab"))
                    .Width(18)
                    .Height(18)
                    .CornerRadius(9)
                    .Padding(1)
                    .HAlign(HorizontalAlignment.Left)
                    .VAlign(VerticalAlignment.Bottom)
                    .IsVisible(tab.IsSleeping)
                    .Grid(row: 0, column: 0))
        )
        .Height(ExpandedTabItemHeight)
        .Padding(16, 14)
        .CornerRadius(10)
        .Set(border => border.Style = GetGlassCardStyle())
        .HAlign(HorizontalAlignment.Stretch)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, tabCollections, onAddUrlToCollection, onToggleFavoriteTab, onCloseTab, onReloadTab, onOpenTabInNewWindow, onMoveTab, tabIndex, tabCount, getTabInstalledWebAppName, getTabInstallableWebAppName, onOpenTabAsWebApp, onInstallTabWebApp);
            ToolTipService.SetToolTip(border, CreateTabToolTip(tab));
            ApplyTabItemBorderState(border, isSelected, IsTabCreationLoading(tab, isLoading));
        });
    }

    private static bool IsTabCreationLoading(BrowserTab tab, bool isLoading)
    {
        return isLoading || string.Equals(tab.Title, "Loading...", StringComparison.Ordinal);
    }

    private static void ApplyTabItemBorderState(Microsoft.UI.Xaml.Controls.Border border, bool isSelected, bool isLoading)
    {
        if (!isLoading)
        {
            if (border.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
            {
                storyboard.Stop();
                border.Tag = null;
            }

            var drawSelectedBorder = isSelected && !BrowserMaterialTheme.IsHighContrast;
            border.BorderThickness = drawSelectedBorder
                ? new Thickness(SelectedTabBorderThickness)
                : new Thickness(1);
            border.BorderBrush = drawSelectedBorder
                ? BrowserMaterialTheme.SelectedStrokeBrush
                : BrowserConstants.SurfaceStrokeColorDefaultBrush;
            border.Opacity = 1;
            return;
        }

        border.BorderThickness = new Thickness(2);

        if (border.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard)
        {
            return;
        }

        var rotateTransform = new RotateTransform
        {
            CenterX = 0.5,
            CenterY = 0.5
        };
        border.BorderBrush = BrowserMaterialTheme.CreateActivityStrokeBrush(rotateTransform);

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(2.2)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, rotateTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Angle");

        var loadingStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        loadingStoryboard.Children.Add(animation);
        border.Tag = loadingStoryboard;
        loadingStoryboard.Begin();
    }

    private static Element BuildMostVisitedItem(
        HistoryItem item,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyList<string> collectionNames,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string, string, string> onAddUrlToCollection,
        bool openInNewTabByDefault)
    {
        return Button(
            Border(
                VStack(8,
                    HStack(8,
                        BuildHistoryIcon(item.Url),
                        BuildVisitBadge(item.VisitCount).HAlign(HorizontalAlignment.Right)
                    ),
                    Border(
                        VStack(4,
                            TextBlock(item.Title)
                                .TextTrimming(TextTrimming.WordEllipsis)
                                .TextWrapping(TextWrapping.Wrap)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 2;
                                    textBlock.MinWidth = 0;
                                    textBlock.FontSize = 12;
                                }),
                            TextBlock(ShortUrl(item.Url))
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Opacity(0.68)
                                .Set(textBlock => textBlock.FontSize = 11),
                            BuildCollectionBadgeRow(collectionNames, maxBadges: 1)
                        )
                    )
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0)
                )
                .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(10, 8)
            .CornerRadius(16)
            .Background(BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(Theme.SurfaceStroke)
            .Width(100)
            .Height(125),
            () => OpenItem(item.Url, openInNewTabByDefault, onOpenHistoryItem, onOpenHistoryItemInNewTab))
            .Set(button => button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, item.Title, tabCollections, onAddUrlToCollection, onOpenHistoryItem, onOpenHistoryItemInNewTab, () => onDeleteHistoryItem(item.Url), "Delete history item"))
            .AutomationName("MostViewed");
    }

    private static string ShortUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.IsNullOrWhiteSpace(uri.Host)
                ? url
                : uri.Host;
        }

        return url;
    }

    private static IReadOnlyList<string> GetCollectionNames(
        IReadOnlyDictionary<string, string[]> collectionMembership,
        string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !collectionMembership.TryGetValue(url, out var collectionNames))
        {
            return [];
        }

        return collectionNames;
    }

    private static Element BuildCollectionBadgeRow(IReadOnlyList<string> collectionNames, int maxBadges)
    {
        if (collectionNames.Count == 0)
        {
            return Border(null).IsVisible(false);
        }

        var badges = collectionNames
            .Take(Math.Max(maxBadges, 1))
            .Select(BuildCollectionBadge)
            .ToList();

        if (collectionNames.Count > maxBadges)
        {
            badges.Add(BuildCollectionBadge($"+{collectionNames.Count - maxBadges}"));
        }

        return FlexRow(badges.ToArray()) with
        {
            ColumnGap = 4,
            RowGap = 4
        };
    }

    private static Element BuildVisitBadge(int count)
    {
        return Border(
            TextBlock(Math.Max(count, 0).ToString())
                .FontSize(11)
                .Foreground(BrowserMaterialTheme.BadgeForegroundBrush)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold))
            .MinWidth(22)
            .Height(22)
            .Padding(6, 0)
            .CornerRadius(999)
            .Background(BrowserMaterialTheme.BadgeFillBrush)
            .WithBorder(BrowserMaterialTheme.SelectedStrokeBrush)
            .Flex(shrink: 0);
    }

    private static Element BuildCollectionBadge(string label)
    {
        return Border(
            TextBlock(label)
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .TextWrapping(TextWrapping.NoWrap)
                .Foreground(BrowserMaterialTheme.BadgeForegroundBrush)
                .Set(textBlock =>
                {
                    textBlock.FontSize = 10;
                    textBlock.MaxLines = 1;
                }))
            .Padding(7, 2)
            .CornerRadius(999)
            .Background(BrowserMaterialTheme.BadgeFillBrush)
            .WithBorder(BrowserMaterialTheme.SelectedStrokeBrush)
            .MaxWidth(88)
            .Flex(shrink: 0);
    }

    private static Element BuildHistoryListItem(
        HistoryItem item,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyList<string> collectionNames,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        Action<string, string, string> onAddUrlToCollection,
        bool openInNewTabByDefault)
    {
        return Border(
            (FlexRow(
                Button(
                    (FlexRow(
                        BuildHistoryIcon(item.Url),
                        VStack(4,
                            TextBlock(item.Title)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                }),
                            TextBlock(item.Url)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Opacity(0.75)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                }),
                            BuildCollectionBadgeRow(collectionNames, maxBadges: 2)
                        )
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0),
                        TextBlock(item.LastVisitedAt.ToString("g")).FontSize(10)
                            .Opacity(0.7)
                            .Flex(shrink: 0)
                    ) with
                    {
                        ColumnGap = 10
                    })
                    .HAlign(HorizontalAlignment.Stretch),
                    () => OpenItem(item.Url, openInNewTabByDefault, onOpenHistoryItem, onOpenHistoryItemInNewTab))
                    .Padding(0)
                    .Background(new SolidColorBrush(Microsoft.UI.Colors.Transparent))
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
                    .Set(button =>
                    {
                        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title);
                        button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, item.Title, tabCollections, onAddUrlToCollection, onOpenHistoryItem, onOpenHistoryItemInNewTab, () => onDeleteHistoryItem(item.Url), "Delete history item");
                    }),
                IconButton(BrowserConstants.GlyphClose, () => onDeleteHistoryItem(item.Url), "Delete history item", buttonSize: 24, iconSize: 10, useGlass: true)
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 8
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(12, 10)
        .CornerRadius(14)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .WithBorder(Theme.SurfaceStroke)
        .Margin(2, 0, 2, 8)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName("HistoryListItem")
        .WithKey($"history:{item.Url}");
    }

    private static Element BuildFavoriteTabItem(
        FavoriteItem item,
        IReadOnlyList<TabCollection> tabCollections,
        IReadOnlyList<string> collectionNames,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
        Action<string, string, string> onAddUrlToCollection,
        bool openInNewTabByDefault)
    {
        return Border(
            (FlexRow(
                Button(
                    (FlexRow(
                        BuildHistoryIcon(item.Url),
                        VStack(2,
                            TextBlock(item.Title)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                }),
                            TextBlock(item.Url)
                                .TextTrimming(TextTrimming.CharacterEllipsis)
                                .TextWrapping(TextWrapping.NoWrap)
                                .Opacity(0.75)
                                .Set(textBlock =>
                                {
                                    textBlock.MaxLines = 1;
                                    textBlock.MinWidth = 0;
                                }),
                            BuildCollectionBadgeRow(collectionNames, maxBadges: 2)
                        )
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0)
                    ) with
                    {
                        ColumnGap = 8
                    })
                    .HAlign(HorizontalAlignment.Stretch),
                    () => OpenItem(item.Url, openInNewTabByDefault, onOpenFavoriteItem, onOpenFavoriteItemInNewTab))
                    .Padding(0)
                    .Background(new SolidColorBrush(Microsoft.UI.Colors.Transparent))
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
                    .Set(button =>
                    {
                        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title);
                        button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, item.Title, tabCollections, onAddUrlToCollection, onOpenFavoriteItem, onOpenFavoriteItemInNewTab, () => onDeleteFavoriteItem(item.Id), "Remove favorite");
                    }),
                IconButton(BrowserConstants.GlyphClose, () => onDeleteFavoriteItem(item.Id), "Remove favorite", buttonSize: 24, iconSize: 10, useGlass: true)
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 8
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(12, 10)
        .CornerRadius(14)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .WithBorder(Theme.SurfaceStroke)
        .Margin(2, 0, 2, 8)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName("FavoriteItem")
        .WithKey($"favorite:{item.Id}");
    }

    private static void OpenItem(
        string url,
        bool openInNewTabByDefault,
        Action<string> onOpenCurrentTab,
        Action<string> onOpenNewTab)
    {
        if (openInNewTabByDefault)
        {
            onOpenNewTab(url);
            return;
        }

        onOpenCurrentTab(url);
    }

    private static MenuFlyout CreateOpenItemContextFlyout(
        string url,
        Action<string> onOpenCurrentTab,
        Action<string> onOpenNewTab,
        Action? onDeleteItem = null,
        string? deleteText = null,
        string? moveText = null,
        int itemIndex = 0,
        int itemCount = 1,
        Action<int>? onMoveItem = null)
    {
        return CreateOpenItemContextFlyout(
            url,
            string.Empty,
            [],
            null,
            onOpenCurrentTab,
            onOpenNewTab,
            onDeleteItem,
            deleteText,
            moveText,
            itemIndex,
            itemCount,
            onMoveItem);
    }

    private static MenuFlyout CreateOpenItemContextFlyout(
        string url,
        string title,
        IReadOnlyList<TabCollection> tabCollections,
        Action<string, string, string>? onAddUrlToCollection,
        Action<string> onOpenCurrentTab,
        Action<string> onOpenNewTab,
        Action? onDeleteItem = null,
        string? deleteText = null,
        string? moveText = null,
        int itemIndex = 0,
        int itemCount = 1,
        Action<int>? onMoveItem = null)
    {
        var flyout = new MenuFlyout();
        var installedWebApp = FindInstalledWebAppForUrl(url);

        var openItem = new MenuFlyoutItem
        {
            Text = "🌐 Open"
        };
        openItem.Click += (_, _) => onOpenCurrentTab(url);
        flyout.Items.Add(openItem);

        var openInNewTabItem = new MenuFlyoutItem
        {
            Text = "➕ Open in new tab"
        };
        openInNewTabItem.Click += (_, _) => onOpenNewTab(url);
        flyout.Items.Add(openInNewTabItem);

        if (installedWebApp is not null)
        {
            var openAppItem = new MenuFlyoutItem
            {
                Text = $"🚀 Open {installedWebApp.Name} as app"
            };
            openAppItem.Click += (_, _) => WebAppWindowService.Open(installedWebApp);
            flyout.Items.Add(openAppItem);
        }

        if (onAddUrlToCollection is not null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var addToCollectionItem = new MenuFlyoutSubItem
            {
                Text = "🗂️ Add to collection"
            };

            var collections = tabCollections
                .Where(collection => !string.IsNullOrWhiteSpace(collection.Name))
                .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (collections.Length == 0)
            {
                addToCollectionItem.Items.Add(new MenuFlyoutItem
                {
                    Text = "Create a collection first",
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var collection in collections)
                {
                    var collectionName = collection.Name;
                    var collectionItem = new MenuFlyoutItem
                    {
                        Text = collectionName
                    };
                    collectionItem.Click += (_, _) => onAddUrlToCollection(collectionName, url, title);
                    addToCollectionItem.Items.Add(collectionItem);
                }
            }

            flyout.Items.Add(addToCollectionItem);
        }

        if (onMoveItem is not null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(CreateMoveSubItem(
                string.IsNullOrWhiteSpace(moveText) ? "↕️ Move" : moveText,
                itemIndex,
                itemCount,
                onMoveItem));
        }

        if (onDeleteItem is not null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem
            {
                Text = string.IsNullOrWhiteSpace(deleteText) ? "🗑️ Delete" : $"🗑️ {deleteText}"
            };
            deleteItem.Click += (_, _) => onDeleteItem();
            flyout.Items.Add(deleteItem);
        }

        return flyout;
    }

    private static InstalledWebApp? FindInstalledWebAppForUrl(string? rawUrl)
    {
        return InstalledWebAppService
            .GetAll()
            .FirstOrDefault(app => IsUrlWithinAppScope(rawUrl, app));
    }

    private static bool IsUrlWithinAppScope(string? rawUrl, InstalledWebApp app)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var target) ||
            !Uri.TryCreate(app.Scope, UriKind.Absolute, out var scopeUri))
        {
            return false;
        }

        return string.Equals(target.Scheme, scopeUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target.Host, scopeUri.Host, StringComparison.OrdinalIgnoreCase) &&
            target.Port == scopeUri.Port &&
            target.AbsolutePath.StartsWith(scopeUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    private static Element BuildHistoryIcon(string url)
    {
        var iconContent = HasFaviconHost(url)
            ? Image(BrowserUrl.GetFaviconUrl(url))
                .AccessibilityHidden()
                .Width(18)
                .Height(18)
                .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
            : FluentIcon(BrowserConstants.GlyphGlobe, 15);

        return Border(
            iconContent
        )
        .Width(24)
        .Height(24)
        .CornerRadius(6)
        .Background(BrowserMaterialTheme.PillFillBrush)
        .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
        .Padding(2)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0);
    }

    private static Element BuildTabIcon(BrowserTab tab, bool isLoading, bool useTileChrome = true)
    {
        return isLoading
            ? BuildTabLoadingIcon(useTileChrome)
            : BuildTabFavicon(tab, useTileChrome);
    }

    private static Element BuildTabLoadingIcon(bool useTileChrome = true)
    {
        if (!useTileChrome)
        {
            return ProgressRing()
                .Width(15)
                .Height(15)
                .IsActive(true)
                .IsVisible(true)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Flex(shrink: 0);
        }

        return Border(
            ProgressRing()
                .Width(14)
                .Height(14)
                .IsActive(true)
                .IsVisible(true)
        )
        .Width(22)
        .Height(22)
        .CornerRadius(6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .Padding(2)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0);
    }
    private static Element BuildTabFavicon(BrowserTab tab, bool useTileChrome = true)
    {
        var iconContent = HasFaviconHost(tab.Url)
            ? Image(BrowserUrl.GetFaviconUrl(tab.Url))
                .AccessibilityHidden()
                .Width(useTileChrome ? 16 : 17)
                .Height(useTileChrome ? 16 : 17)
                .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
            : FluentIcon(BrowserConstants.GlyphGlobe, useTileChrome ? 14 : 15);

        if (!useTileChrome)
        {
            return iconContent
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Flex(shrink: 0);
        }

        return Border(
            iconContent
        )
        .Width(30)
        .Height(30)
        .CornerRadius(8)
        .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)))
        .Padding(5)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0)
        .Set(border =>
        {
            border.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 1, 10);
        });
    }

    private static bool HasFaviconHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static Element FluentIcon(string glyph, double size = 14)
    {
        return TextBlock(glyph)
            .Set(textBlock =>
            {
                var useEmojiFont = false;
                if (!string.IsNullOrEmpty(glyph))
                {
                    // common red-heart emoji codepoints or surrogate pairs -> use emoji font
                    if (glyph.IndexOf('\u2764') >= 0 || glyph.IndexOf('\uFE0F') >= 0)
                    {
                        useEmojiFont = true;
                    }
                    else if (glyph.Length > 0 && char.IsSurrogate(glyph[0]))
                    {
                        useEmojiFont = true;
                    }
                }

                textBlock.FontFamily = useEmojiFont
                    ? new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji")
                    : BrowserConstants.IconFontFamily;
                textBlock.FontSize = size;
            })
            .VAlign(VerticalAlignment.Center)
            .HAlign(HorizontalAlignment.Center);
    }

    private static ButtonElement IconButton(
        string glyph,
        Action onClick,
        string automationName,
        double buttonSize = 30,
        double iconSize = 14,
        bool useGlass = false)
    {
        return Button(FluentIcon(glyph, iconSize), onClick)
            .AutomationName(automationName)
            .ToolTip(automationName)
            .Width(buttonSize)
            .Height(buttonSize)
            .Padding(0)
            .Set(button =>
            {
                // Reactor can recycle a native Button when toolbar items move. Clear any
                // flyout left by a previous role before specialized buttons attach theirs.
                button.Flyout = null;

                if (useGlass)
                {
                    button.Style = GetGlassIconButtonStyle();
                    ApplyGlassButtonDepth(button);
                }
            });
    }

    private static void ApplyGlassButtonDepth(Microsoft.UI.Xaml.Controls.Button button)
    {
        button.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
        button.Translation = new System.Numerics.Vector3(0, 1, 12);
    }

    private static Style GetGlassIconButtonStyle()
    {
        return new Style(typeof(Microsoft.UI.Xaml.Controls.Button))
        {
            Setters =
            {
                new Setter(Microsoft.UI.Xaml.Controls.Control.BackgroundProperty, BrowserMaterialTheme.PillFillBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Control.BorderBrushProperty, BrowserMaterialTheme.GlassStrokeBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Control.BorderThicknessProperty, new Thickness(1)),
                new Setter(Microsoft.UI.Xaml.Controls.Control.CornerRadiusProperty, new CornerRadius(10))
            }
        };
    }

    private static Style GetGlassCardStyle()
    {
        return new Style(typeof(Microsoft.UI.Xaml.Controls.Border))
        {
            Setters =
            {
                new Setter(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty, BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty, BrowserConstants.SurfaceStrokeColorDefaultBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Microsoft.UI.Xaml.Controls.Border.CornerRadiusProperty, new CornerRadius(12))
            }
        };
    }

    private static Style GetCollapsedTabGlassCardStyle()
    {
        return new Style(typeof(Microsoft.UI.Xaml.Controls.Border))
        {
            Setters =
            {
                new Setter(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)),
                new Setter(Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty, BrowserConstants.SurfaceStrokeColorDefaultBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Microsoft.UI.Xaml.Controls.Border.CornerRadiusProperty, new CornerRadius(20))
            }
        };
    }

    private static StackPanel CreateTabToolTip(BrowserTab tab)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = tab.Title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320
                },
                new TextBlock
                {
                    Text = tab.Url,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320,
                    Opacity = 0.8
                },
                new TextBlock
                {
                    Text = tab.IsSleeping ? "Sleeping — resumes when selected" : "Active in memory",
                    Opacity = 0.7
                }
            }
        };
    }
}
