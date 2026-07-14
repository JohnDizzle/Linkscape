using AI_Agent.Browser;
using AI_Agent.Models;

namespace Browser.Components;

internal static class BrowserChrome
{
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";
    private const string BackdropGradientPresetDefault = "Default";
    private const double RailHeaderHeight = 0;
    private const double CommandCenterBladeHeight = 300;
    private const double CommandCenterFooterHeight = 108;
    private const double CommandCenterCardHeight = 150;
    private const double CompactTabsCardHeight = 84;
    private const double RailSectionSpacing = 14;
    private const double ExpandedTabItemHeight = 36;
    private const double CollapsedTabItemHeight = 40;
    private const double CollapsedRailWidth = 56;
    private static Style? _expandedTabItemContainerStyle;
    private static Style? _collapsedTabItemContainerStyle;

    internal sealed class SettingGridItem : IReactorKeyed
    {
        public required string Key { get; init; }

        public string Value { get; set; } = string.Empty;

        string IReactorKeyed.Key => Key;
    }

    public static Element BuildTitleBar(
        BrowserTab selectedTab,
        string addressText,
        bool isTabsCollapsed,
        bool canGoBack,
        bool canGoForward,
        Action onToggleTabs,
        Action onBack,
        Action onRefresh,
        Action onForward,
        Action<string> onAddressChanged,
        Action<string> onSubmitAddress,
        Action<string> onNavigateCurrentTab,
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider,
        Action onToggleFavorite,
        Action onAddTab,
        Action onCloseTab)
    {
        return Border(
            FlexRow(
                HStack(
                    IconButton(BrowserConstants.GlyphMenu, onToggleTabs, isTabsCollapsed ? "Expand tabs" : "Collapse tabs to icons"),
                    IconButton(BrowserConstants.GlyphAdd, onAddTab, "Add tab"),
                    IconButton(BrowserConstants.GlyphClose, onCloseTab, "Close active tab")
                ).Margin(0, 0, 8, 0),
                IconButton(BrowserConstants.GlyphBack, onBack, "Go back").IsEnabled(canGoBack),
                IconButton(BrowserConstants.GlyphForward, onForward, "Go forward").IsEnabled(canGoForward),
                IconButton(BrowserConstants.GlyphRefresh, onRefresh, "Refresh page"),
                Border(
                    AutoSuggestBox(addressText, onAddressChanged, submitted => onSubmitAddress(submitted))
                    .AutomationName("Address Bar") with
                    {
                        PlaceholderText = "Search or enter web address"
                    }
                )
                .Padding(0)
                .Flex(grow: 1, basis: 0),

                BuildSearchProviderButton(selectedSearchProviderKey, searchProviders, onSelectSearchProvider),
                IconButton(BrowserConstants.GlyphHome, () => onNavigateCurrentTab(BrowserConstants.HomeUrl), "Go home"),
                IconButton(
                    selectedTab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                    onToggleFavorite,
                    "Toggle favorite")
               
            ) with
            {
                ColumnGap = 8
            }
        )
        .Padding(8, 6, 8, 6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(shrink: 0);
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
            .Background(Theme.LayerFill)
            .WithBorder(Theme.SurfaceStroke)
            .Padding(2),
            () => { })
            .AutomationName("Search provider")
            .Width(30)
            .Height(30)
            .Padding(0)
            .Set(button => button.Flyout = flyout); 
    }

    private static MenuFlyout CreateTabContextFlyout(
        BrowserTab tab,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        var flyout = new MenuFlyout();

        var favoriteItem = new MenuFlyoutItem
        {
            Text = tab.IsFavorite ? "Remove Favorite" : "Add Favorite"
        };
        favoriteItem.Click += (_, _) => onToggleFavoriteTab(tab.Id);

        var reloadItem = new MenuFlyoutItem
        {
            Text = "Reload"
        };
        reloadItem.Click += (_, _) => onReloadTab(tab.Id);

        var closeItem = new MenuFlyoutItem
        {
            Text = "Close"
        };
        closeItem.Click += (_, _) => onCloseTab(tab.Id);

        flyout.Items.Add(favoriteItem);
        flyout.Items.Add(reloadItem);
        flyout.Items.Add(closeItem);

        return flyout;
    }

    private static MenuFlyout CreateHistoryImportFlyout(
        IReadOnlyList<string> browserNames,
        Action onImportAllHistory,
        Action<string> onImportBrowserHistory,
        bool isImportRunning)
    {
        var flyout = new MenuFlyout();

        var allBrowsersItem = new MenuFlyoutItem
        {
            Text = isImportRunning ? "Import running…" : "All browsers",
            IsEnabled = !isImportRunning
        };
        allBrowsersItem.Click += (_, _) => onImportAllHistory();
        flyout.Items.Add(allBrowsersItem);

        if (browserNames.Count == 0)
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

        foreach (var browserName in browserNames)
        {
            var importBrowserItem = new MenuFlyoutItem
            {
                Text = browserName,
                IsEnabled = !isImportRunning
            };
            importBrowserItem.Click += (_, _) => onImportBrowserHistory(browserName);
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
    Action<int> onSelect,
    Action<string> onToggleFavoriteTab,
    Action<string> onCloseTabFromContextMenu,
    Action<string> onReloadTab,
    string activeCommandCenterSection,
    bool isCommandCenterExpanded,
    IReadOnlyList<HistoryItem> mostVisitedItems,
    IReadOnlyList<HistoryItem> recentHistoryItems,
    string historyFilter,
    string historyImportStatus,
    IReadOnlyList<string> historyImportBrowserNames,
    IReadOnlyList<FavoriteItem> favoriteItems,
    string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyList<string> favoritesImportBrowserNames,
        bool isCommandCenterBusy,
        string commandCenterBusyText,
    IReadOnlyDictionary<string, string> settingsSnapshot,
    Action<string, string> onSaveSettingValue,
    Action<string> onHistoryFilterChanged,
    Action<string> onFavoritesFilterChanged,
    Action onImportHistory,
    Action<string> onImportBrowserHistory,
    Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action onDeleteAllFavorites,
    Action<string> onOpenHistoryItem,
    Action<string> onToggleCommandCenter,
    Action onToggleCommandCenterExpanded,
    bool isRailTabsExpanded,
    Action onMaximizeTabs,
    Action onMinimizeTabs,
    Action onDismissCommandCenter)
    {
        var selectedTab = tabs.FirstOrDefault(tab => string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal)) ?? tabs[0];
        var isSelectedTabLoading = isLoading && string.Equals(selectedTab.Id, selectedTabId, StringComparison.Ordinal);
        var collapsedCommandCenterHeight = CommandCenterCardHeight;

        if (!string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            collapsedCommandCenterHeight += CommandCenterBladeHeight + 4;
        }

        var railWidth = isTabsCollapsed ? CollapsedRailWidth : 400;
        var tabList = (ListView<BrowserTab>(
            tabs,
            (tab, _) =>
            {
                var isTabLoading = isLoading &&
                    string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);

                return isTabsCollapsed
                    ? BuildCollapsedTabItem(tab, isTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab)
                    : BuildExpandedTabItem(tab, isTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab);
            }) with
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelect,
            SelectionMode = ListViewSelectionMode.Single,
        })
        .WithKey(isTabsCollapsed ? "CollapsedTabRailList" : "ExpandedTabRailList")
        .Set(listView =>
        {
            listView.IsItemClickEnabled = true;
            listView.Padding = isTabsCollapsed
                ? new Thickness(0)
                : new Thickness(4);
            listView.BorderThickness = new Thickness(0);
            listView.HorizontalContentAlignment = isTabsCollapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch;
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
                listView,
                isTabsCollapsed ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Disabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(listView, ScrollMode.Enabled);
            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(listView, ScrollMode.Disabled);

            listView.ItemContainerStyle = GetTabItemContainerStyle(isTabsCollapsed);
        });

        if (isTabsCollapsed)
        {
            return Border(
                tabList
                    .Flex(grow: 1, basis: 0)
            )
            .Width(railWidth)
            .Set(border =>
            {
                border.Background = BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush;
                border.BorderThickness = new Thickness(0);
            })
            .Flex(shrink: 0)
            .VAlign(VerticalAlignment.Stretch);
        }

        bool showCompactTabsCard = !isRailTabsExpanded;

        var openTabsCard = showCompactTabsCard
            ? BuildCompactTabsCard(onMaximizeTabs)
                .Height(CompactTabsCardHeight)
                .Flex(grow: 0, shrink: 0, basis: CompactTabsCardHeight)
            : BuildRailSectionCard(
                "Open Tabs",
                BuildExpandableTabsList(tabList, onMinimizeTabs))
                .Flex(grow: 1, shrink: 1, basis: 0);

        return Border(
            FlexColumn(
                Border(null)
                    .Height(RailHeaderHeight)
                    .IsVisible(false),
                openTabsCard,
                BuildCommandCenterHost(
                    activeCommandCenterSection,
                    isCommandCenterExpanded,
                    mostVisitedItems,
                    recentHistoryItems,
                    historyFilter,
                    historyImportStatus,
                    historyImportBrowserNames,
                    favoriteItems,
                    favoritesFilter,
                    favoritesImportStatus,
                    favoritesImportBrowserNames,
                    isCommandCenterBusy,
                    commandCenterBusyText,
                    settingsSnapshot,
                    onSaveSettingValue,
                    onHistoryFilterChanged,
                    onFavoritesFilterChanged,
                    onImportHistory,
                    onImportBrowserHistory,
                    onDeleteAllHistory,
                    onImportFavorites,
                    onImportBrowserFavorites,
                    onDeleteAllFavorites,
                    onOpenHistoryItem,
                    onToggleCommandCenter,
                    onToggleCommandCenterExpanded,
                    onDismissCommandCenter)
                .Flex(grow: isCommandCenterExpanded ? 1 : 0, shrink: 1, basis: isCommandCenterExpanded ? 0 : collapsedCommandCenterHeight)
                .VAlign(isCommandCenterExpanded ? VerticalAlignment.Stretch : VerticalAlignment.Bottom)
            ) with
            {
                RowGap = RailSectionSpacing
            }
        )
        .Padding(12)
        .Width(railWidth)
        .Set(border =>
        {
            border.Background = BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush;
        })
        .WithBorder(Theme.SurfaceStroke)
        .Flex(shrink: 0)
        .VAlign(VerticalAlignment.Stretch);
    }

    private static Element BuildCommandCenterHost(
        string activeCommandCenterSection,
        bool isCommandCenterExpanded,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        string historyImportStatus,
        IReadOnlyList<string> historyImportBrowserNames,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyList<string> favoritesImportBrowserNames,
        bool isCommandCenterBusy,
        string commandCenterBusyText,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action<string> onFavoritesFilterChanged,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action onDeleteAllFavorites,
        Action<string> onOpenHistoryItem,
        Action<string> onToggleCommandCenter,
        Action onToggleCommandCenterExpanded,
        Action onDismissCommandCenter)
    {
        var blade = BuildCommandCenterBlade(
            activeCommandCenterSection,
            isCommandCenterExpanded,
            mostVisitedItems,
            recentHistoryItems,
            historyFilter,
            historyImportStatus,
            historyImportBrowserNames,
            favoriteItems,
            favoritesFilter,
            favoritesImportStatus,
            favoritesImportBrowserNames,
            isCommandCenterBusy,
            settingsSnapshot,
            onSaveSettingValue,
            onHistoryFilterChanged,
            onFavoritesFilterChanged,
            onImportHistory,
            onImportBrowserHistory,
            onDeleteAllHistory,
            onImportFavorites,
            onImportBrowserFavorites,
            onDeleteAllFavorites,
            onOpenHistoryItem,
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
                    .Height(CommandCenterFooterHeight), CommandCenterCardHeight, isCommandCenterBusy))
            .MinHeight(0)
            .Flex(grow: isCommandCenterExpanded ? 1 : 0, shrink: 1, basis: 0);
    }

    private static Element BuildCommandCenterBlade(
        string activeCommandCenterSection,
        bool isCommandCenterExpanded,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        string historyImportStatus,
        IReadOnlyList<string> historyImportBrowserNames,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyList<string> favoritesImportBrowserNames,
        bool isCommandCenterBusy,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action<string> onFavoritesFilterChanged,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action onDeleteAllHistory,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action onDeleteAllFavorites,
        Action<string> onOpenHistoryItem,
        Action onToggleCommandCenterExpanded,
        Action onDismissCommandCenter)
    {
        if (string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            return Border(null).IsVisible(false);
        }

        Element content = activeCommandCenterSection switch
        {
            "History" => BuildHistoryBladeContent(recentHistoryItems, historyFilter, historyImportStatus, historyImportBrowserNames, isCommandCenterBusy, onHistoryFilterChanged, onImportHistory, onImportBrowserHistory, onDeleteAllHistory, onOpenHistoryItem, isCommandCenterExpanded),
            "Recent" => BuildRecentBladeContent(recentHistoryItems, onOpenHistoryItem, isCommandCenterExpanded),
            "MostVisited" => BuildMostVisitedBladeContent(mostVisitedItems, onOpenHistoryItem, isCommandCenterExpanded),
            "Favorites" => BuildFavoritesBladeContent(favoriteItems, favoritesFilter, favoritesImportStatus, favoritesImportBrowserNames, isCommandCenterBusy, onFavoritesFilterChanged, onImportFavorites, onImportBrowserFavorites, onDeleteAllFavorites, onOpenHistoryItem, isCommandCenterExpanded),
            "Settings" => BuildSettingsBladeContent(settingsSnapshot),
            "Backdrop" => BuildBackdropBladeContent(settingsSnapshot, onSaveSettingValue),
            "Chat" => BuildPlaceholderBladeContent("Chat", "Chat agent entry point is reserved for a later step."),
            _ => Border(null)
        };

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
                        iconSize: 8)
                    .Margin(2, 0, 2, 0),
                    IconButton(
                        BrowserConstants.GlyphClose,
                        onDismissCommandCenter,
                        "Dismiss command center blade",
                        buttonSize: 24,
                        iconSize: 8)
                ).HAlign(HorizontalAlignment.Right),
                ScrollViewer(content)
                    .Set(scrollViewer =>
                    {
                        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                        scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                    })
                    .VAlign(VerticalAlignment.Stretch)
                    .MinHeight(0)
                    .Flex(grow: 1, shrink: 1, basis: 0)
            )
            .MinHeight(0)
        )
            .Padding(14)
            .CornerRadius(14)
            .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
            .WithBorder(Theme.SurfaceStroke)
            .Margin(0, isCommandCenterExpanded ? 6 : 0, 0, 4)
            .MinHeight(0);

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
                    BuildCommandCenterButton("Settings", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Favorites", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Backdrop", activeCommandCenterSection, onToggleCommandCenter),
                    BuildCommandCenterButton("Chat", activeCommandCenterSection, onToggleCommandCenter, "Chat")
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
        .Padding(0)
        .Background(BrowserConstants.CardBackgroundFillColorDefaultBrush)
        .Height(CommandCenterFooterHeight);
    }

    private static Element BuildCommandCenterButton(
        string section,
        string activeCommandCenterSection,
        Action<string> onToggleCommandCenter,
        string? label = null)
    {
        var isActive = string.Equals(activeCommandCenterSection, section, StringComparison.Ordinal);

        return Button(label ?? section, () => onToggleCommandCenter(section))
            .Background(isActive ? BrowserConstants.SubtleFillColorSecondaryBrush : BrowserConstants.LayerFillDefaultBrush)
            .CornerRadius(10)
            .Padding(6)
            .Flex(grow: 1, basis: 0);
    }

    private static Element BuildRailSectionCard(
        string title,
        Element content,
        double? fixedHeight = null,
        bool isBusy = false)
    {
        var card = Border(
            VStack(10,
                TextBlock(title)
                    .Set(textBlock =>
                    {
                        textBlock.FontFamily = BrowserConstants.TextFontFamily;
                        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    }),
                Border(content)
                    .Padding(8)
                    .CornerRadius(10)
                    .Background(BrowserConstants.LayerFillDefaultBrush)
                    .WithBorder(Theme.SurfaceStroke)
                    .Flex(grow: 1, basis: 0)
            )
        )
        .Padding(12)
        .CornerRadius(16)
        .Background(BrowserConstants.LayerFillAltBrush)
        .WithBorder(Theme.SurfaceStroke)
        .Margin(0, 0, 0, 6)
        .Set(border =>
        {
            if (string.Equals(title, "Command Center", StringComparison.Ordinal))
            {
                ApplyCommandCenterBusyState(border, isBusy);
            }
        });

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

            border.BorderThickness = new Thickness(0);
            border.ClearValue(Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty);
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
        border.BorderBrush = CreateRainbowBorderBrush(rotateTransform);

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

    private static Brush CreateRainbowBorderBrush(RotateTransform rotateTransform)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            RelativeTransform = rotateTransform,
            GradientStops = new GradientStopCollection
            {
                new() { Color = Microsoft.UI.Colors.Red, Offset = 0.00 },
                new() { Color = Microsoft.UI.Colors.Orange, Offset = 0.18 },
                new() { Color = Microsoft.UI.Colors.Yellow, Offset = 0.34 },
                new() { Color = Microsoft.UI.Colors.LimeGreen, Offset = 0.50 },
                new() { Color = Microsoft.UI.Colors.DeepSkyBlue, Offset = 0.66 },
                new() { Color = Microsoft.UI.Colors.MediumPurple, Offset = 0.82 },
                new() { Color = Microsoft.UI.Colors.HotPink, Offset = 1.00 }
            }
        };
    }

    private static Element BuildCompactTabsCard(Action onShowTabs)
    {
        return Border(
            HStack(8,
                FluentIcon(BrowserConstants.GlyphMenu, 18),
                TextBlock("Tabs")
                    .Set(textBlock =>
                    {
                        textBlock.FontFamily = BrowserConstants.TextFontFamily;
                        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    })
                    .Flex(grow: 1, basis: 0)
            )
        )
        .Padding(12)
        .CornerRadius(16)
        .Background(BrowserConstants.LayerFillAltBrush)
        .WithBorder(Theme.SurfaceStroke)
        .Set(border =>
        {
            border.DoubleTapped += (_, _) => onShowTabs();
            ToolTipService.SetToolTip(border, "Double-click to show the full tabs list.");
        });
    }

    private static Element BuildExpandableTabsList(Element tabList, Action onMinimizeTabs)
    {
        return Border(tabList)
            .Set(border =>
            {
                border.DoubleTapped += (_, _) => onMinimizeTabs();
                ToolTipService.SetToolTip(border, "Double-click to collapse to the compact Tabs card.");
            });
    }

    private static Element BuildHistoryBladeContent(
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        string historyImportStatus,
        IReadOnlyList<string> historyImportBrowserNames,
        bool isImportRunning,
        Action<string> onHistoryFilterChanged,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action onDeleteAllHistory,
        Action<string> onOpenHistoryItem,
        bool isCommandCenterExpanded)
    {
        var historyItems = recentHistoryItems
            .Select(item => BuildHistoryListItem(item, onOpenHistoryItem))
            .ToArray();

        return VStack(18,
            TextBlock("History")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                ,
            HStack(14,
                Border(
                    AutoSuggestBox(historyFilter, onHistoryFilterChanged, submitted => onHistoryFilterChanged(submitted))
                   .AutomationName("History Filter")
                    with
                        {
                            PlaceholderText = "Filter history"
                        }
                ).HAlign(HorizontalAlignment.Left)
                .Padding(0)
                .Flex(grow: 1, basis: 0),
                HStack(8,
                    Button("Import", () => { })
                        .IsEnabled(!isImportRunning)
                        .Set(button => button.Flyout = CreateHistoryImportFlyout(historyImportBrowserNames, onImportHistory, onImportBrowserHistory, isImportRunning)),
                    Button("Delete all history", onDeleteAllHistory)
                        .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                        .AutomationName("Delete all history")
                ).HAlign(HorizontalAlignment.Right)
            ),
            string.IsNullOrWhiteSpace(historyImportStatus)
                ? Border(null).IsVisible(false)
                : Border(
                    TextBlock(historyImportStatus)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
            historyItems.Length == 0
                ? Border(
                    TextBlock("No history items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : VStack(0, historyItems));
    }

    private static Element BuildRecentBladeContent(
        IReadOnlyList<HistoryItem> recentHistoryItems,
        Action<string> onOpenHistoryItem,
        bool isCommandCenterExpanded)
    {
        var recentItems = recentHistoryItems
            .Select(item => BuildHistoryListItem(item, onOpenHistoryItem))
            .ToArray();

        return VStack(10,
            TextBlock("Recent")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            recentItems.Length == 0
                ? Border(
                    TextBlock("No recent items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : VStack(0, recentItems));
    }

    private static Element BuildMostVisitedBladeContent(
        IReadOnlyList<HistoryItem> mostVisitedItems,
        Action<string> onOpenHistoryItem,
        bool isCommandCenterExpanded)
    {
        var topItems = mostVisitedItems.ToArray();
        var rows = new List<Element>();

        for (var index = 0; index < topItems.Length; index += 2)
        {
            var cards = new List<Element>();

            for (var column = index; column < Math.Min(index + 2, topItems.Length); column++)
            {
                cards.Add(BuildMostVisitedItem(topItems[column], onOpenHistoryItem)
                    .Flex(grow: 1, basis: 0));
            }

            while (cards.Count < 2)
            {
                cards.Add(Border(null)
                    .Width(100)
                    .Height(92)
                    .Flex(grow: 1, basis: 0));
            }

            rows.Add(HStack(8, cards.ToArray()));
        }

        return VStack(16,
            TextBlock("Most visited")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            rows.Count == 0
                ? Border(
                    TextBlock("No most-visited items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : VStack(10, rows.ToArray())
                    .Padding(0, 0, 6, 0));
    }

    private static Element BuildFavoritesBladeContent(
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyList<string> favoritesImportBrowserNames,
        bool isImportRunning,
        Action<string> onFavoritesFilterChanged,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action onDeleteAllFavorites,
        Action<string> onOpenFavoriteItem,
        bool isCommandCenterExpanded)
    {
        var favoriteRows = new List<Element>();

        for (var index = 0; index < favoriteItems.Count; index++)
        {
            favoriteRows.Add(BuildFavoriteTabItem(favoriteItems[index], onOpenFavoriteItem));
        }

        return VStack(10,
            TextBlock("Favorites")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            HStack(14,
                Border(
                    AutoSuggestBox(favoritesFilter, onFavoritesFilterChanged, submitted => onFavoritesFilterChanged(submitted))
                    .AutomationName("Favorites Filter") with
                    {
                        PlaceholderText = "Filter favorites"
                    }
                )
                .HAlign(HorizontalAlignment.Left)
                .Padding(0)
                .Flex(grow: 1, basis: 0),
                HStack(8,
                    Button("Import", () => { })
                        .IsEnabled(!isImportRunning)
                        .Set(button => button.Flyout = CreateHistoryImportFlyout(favoritesImportBrowserNames, onImportFavorites, onImportBrowserFavorites, isImportRunning)),
                    Button("Delete all favorites", onDeleteAllFavorites)
                        .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
                        .AutomationName("Delete all favorites")
                ).HAlign(HorizontalAlignment.Right)
            ),
            string.IsNullOrWhiteSpace(favoritesImportStatus)
                ? Border(null).IsVisible(false)
                : Border(
                    TextBlock(favoritesImportStatus)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8)
                .CornerRadius(8)
                .Background(BrowserConstants.SubtleFillColorSecondaryBrush),
            favoriteItems.Count == 0
                ? Border(
                    TextBlock("No favorites yet. Star a tab or import bookmarks from another browser.")
                        .Opacity(0.7)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8, 4)
                : VStack(6, favoriteRows.ToArray()));
    }

    private static Element BuildSettingsBladeContent(IReadOnlyDictionary<string, string> settingsSnapshot)
    {
        var settingsItems = settingsSnapshot
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new SettingGridItem
            {
                Key = entry.Key,
                Value = entry.Value
            })
            .ToList();

        return VStack(10,
            TextBlock("Settings")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            TextBlock("Current values from settings.db.")
                .TextWrapping(TextWrapping.Wrap)
                .Opacity(0.76),
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

    private static Element BuildBackdropBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue)
    {
        var selectedBackdropPreset = settingsSnapshot.TryGetValue(BackdropGradientPresetSettingKey, out var configuredPreset)
            ? NormalizeBackdropGradientPreset(configuredPreset)
            : BackdropGradientPresetDefault;

        return VStack(10,
            TextBlock("Backdrop gradient")
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
            TextBlock("Choose a preset tint for the Acrylic app backdrop.")
                .TextWrapping(TextWrapping.Wrap)
                .Opacity(0.76),
            BuildBackdropPresetPicker(selectedBackdropPreset, onSaveSettingValue)
        );
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

        return Button(normalizedPreset, () => onSaveSettingValue(BackdropGradientPresetSettingKey, normalizedPreset))
            .Background(isSelected ? BrowserConstants.AccentFillColorDefaultBrush : BrowserConstants.LayerFillDefaultBrush)
            .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
            .CornerRadius(999)
            .Padding(12, 6)
            .AutomationName("Select backdrop gradient preset: " + normalizedPreset)
            .MinWidth(72);
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

    private static Style CreateTabItemContainerStyle(bool isTabsCollapsed)
    {
        var style = new Style(typeof(ListViewItem));

        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, isTabsCollapsed ? 0d : 280d));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, isTabsCollapsed ? CollapsedTabItemHeight : double.NaN));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, isTabsCollapsed
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Stretch));

        return style;
    }
    private static Element BuildCollapsedTabItem(
        BrowserTab tab,
        bool isLoading,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        return Border(
            BuildTabIcon(tab, isLoading)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
        )
        .Width(CollapsedTabItemHeight)
        .Height(CollapsedTabItemHeight)
        .Padding(4)
        .CornerRadius(8)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, onToggleFavoriteTab, onCloseTab, onReloadTab);
            ToolTipService.SetToolTip(border, CreateTabToolTip(tab));
        });
    }



    private static Element BuildExpandedTabItem(
        BrowserTab tab,
        bool isLoading,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        return Border(
            HStack(8,
                BuildTabIcon(tab, isLoading),
                Border(
                    TextBlock(tab.Title)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .TextWrapping(TextWrapping.NoWrap)
                        .Set(textBlock =>
                        {
                            textBlock.FontFamily = BrowserConstants.TextFontFamily;
                            textBlock.MaxLines = 1;
                            textBlock.MinWidth = 0;
                        })
                )
                .MinWidth(0)
                .Flex(grow: 1, basis: 0),
                InfoBadge(tab.VisitedCount)
            )
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Height(ExpandedTabItemHeight)
        .Padding(8, 6)
        .CornerRadius(6)
        .HAlign(HorizontalAlignment.Stretch)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, onToggleFavoriteTab, onCloseTab, onReloadTab);
            ToolTipService.SetToolTip(border, CreateTabToolTip(tab));
        });
    }

    private static Element BuildMostVisitedItem(HistoryItem item, Action<string> onOpenHistoryItem)
    {
        return Button(
            Border(
                VStack(8,
                    HStack(8,
                        BuildHistoryIcon(item.Url),
                        InfoBadge(item.VisitCount).HAlign(HorizontalAlignment.Right)
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
                                .Set(textBlock => textBlock.FontSize = 11)
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
            () => onOpenHistoryItem(item.Url)).AutomationName("MostViewed");
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

    private static Element BuildHistoryListItem(HistoryItem item, Action<string> onOpenHistoryItem)
    {
        return Button(
            Border(
                HStack(10,
                    BuildHistoryIcon(item.Url),
                    VStack(4,
                        TextBlock(item.Title)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap),
                        TextBlock(item.Url)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap)
                            .Opacity(0.75)
                    )
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0),
                    TextBlock(item.LastVisitedAt.ToString("g"))
                        .Opacity(0.7)
                )
            )
            .Padding(12, 10)
            .CornerRadius(14)
            .Background(BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(Theme.SurfaceStroke)
            .Margin(0, 0, 0, 8),
            () => onOpenHistoryItem(item.Url)).AutomationName("HistoryListItem");
    }

    private static Element BuildFavoriteTabItem(FavoriteItem item, Action<string> onOpenFavoriteItem)
    {
        return Button(
            Border(
                HStack(8,
                    BuildHistoryIcon(item.Url),
                    VStack(2,
                        TextBlock(item.Title)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap),
                        TextBlock(item.Url)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap)
                            .Opacity(0.75)
                    )
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0)
                )
            )
            .Padding(8, 6)
            .CornerRadius(8),
            () => onOpenFavoriteItem(item.Url)).AutomationName("FavoriteItem");
    }

    private static Element BuildHistoryIcon(string url)
    {
        return Border(
            Image(BrowserUrl.GetDomainFaviconUrl(url))
                .AccessibilityHidden()
                .Width(18)
                .Height(18)
                .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
        )
        .Width(24)
        .Height(24)
        .CornerRadius(6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .Padding(2)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0);
    }

    private static Element BuildTabIcon(BrowserTab tab, bool isLoading)
    {
        return isLoading
            ? BuildTabLoadingIcon()
            : BuildTabFavicon(tab);
    }

    private static Element BuildTabLoadingIcon()
    {
        return Border(
            ProgressRing()
                .Width(16)
                .Height(16)
                .IsActive(true)
                .IsVisible(true)
        )
        .Width(24)
        .Height(24)
        .CornerRadius(6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .Padding(2)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0);
    }
    private static Element BuildTabFavicon(BrowserTab tab)
    {
        return Border(
            tab.IsHomeTab
                ? FluentIcon(BrowserConstants.GlyphHome, 14)
                : Image(BrowserUrl.GetDomainFaviconUrl(tab.Url))
                    .AccessibilityHidden()
                    .Width(18)
                    .Height(18)
                    .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
        )
        .Width(24)
        .Height(24)
        .CornerRadius(6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .Padding(2)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Flex(shrink: 0);
    }

    private static Element FluentIcon(string glyph, double size = 14)
    {
        return TextBlock(glyph)
            .Set(textBlock =>
            {
                textBlock.FontFamily = BrowserConstants.IconFontFamily;
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
        double iconSize = 14)
    {
        return Button(FluentIcon(glyph, iconSize), onClick)
            .AutomationName(automationName)
            .Width(buttonSize)
            .Height(buttonSize)
            .Padding(0);
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
                }
            }
        };
    }
}