using LinkScape.Browser;
using LinkScape.Models;

namespace Browser.Components;

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
    private const double ActiveTabHeaderMinHeight = 134;
    private const double RailSectionSpacing = 14;
    private const double ExpandedTabItemHeight = 68;
    private const double CollapsedTabItemHeight = 40;
    private const double CollapsedRailWidth = 56;
    private const double TabItemHoverScale = 1.04;
    private const double TabItemHorizontalInset = 4;
    private const double SelectedTabBorderThickness = 1;
    private static Style? _expandedTabItemContainerStyle;
    private static Style? _collapsedTabItemContainerStyle;
    private static Style? _glassIconButtonStyle;
    private static Style? _glassCardStyle;
    private static Style? _collapsedTabGlassCardStyle;
    
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

    public static TimeSpan RailToggleDuration => TimeSpan.FromMilliseconds(RailToggleDurationMilliseconds);

    public static Element BuildTitleBar(
        BrowserTab selectedTab,
        string addressText,
        string homeUrl,
        bool isTabsCollapsed,
        bool canGoBack,
        bool canGoForward,
        Action onToggleTabs,
        Action onBack,
        Action onRefresh,
        Action onForward,
        Action<string> onAddressChanged,
        Action<string> onSubmitAddress,
        Action<Microsoft.UI.Xaml.Controls.AutoSuggestBox> onAddressBoxReady,
        Action<string> onNavigateCurrentTab,
        string selectedSearchProviderKey,
        IReadOnlyList<BrowserSearchProvider> searchProviders,
        Action<string> onSelectSearchProvider,
        Action onSetCurrentPageAsHome,
        Action onToggleFavorite,
        Action onShowCommandCenterSettings,
        Action onAddTab,
        Action onCloseTab)
    {
        return Border(
            (FlexRow(
                IconButton(BrowserConstants.GlyphMenu, onToggleTabs, isTabsCollapsed ? "Expand tabs" : "Collapse tabs to icons", buttonSize: 32, iconSize: 15, useGlass: true),
                IconButton(BrowserConstants.GlyphAdd, onAddTab, "Add tab", buttonSize: 32, iconSize: 15, useGlass: true),
                IconButton(BrowserConstants.GlyphClose, onCloseTab, "Close active tab", buttonSize: 32, iconSize: 15, useGlass: true),
                IconButton(BrowserConstants.GlyphBack, onBack, "Go back", buttonSize: 32, iconSize: 15, useGlass: true).IsEnabled(canGoBack),
                IconButton(BrowserConstants.GlyphForward, onForward, "Go forward", buttonSize: 32, iconSize: 15, useGlass: true).IsEnabled(canGoForward),
                IconButton(BrowserConstants.GlyphRefresh, onRefresh, "Refresh page", buttonSize: 32, iconSize: 15, useGlass: true),
                BuildAddressBar(selectedTab, addressText, onAddressChanged, onSubmitAddress, onAddressBoxReady)
                .Flex(grow: 1, basis: 0),

                
                IconButton(BrowserConstants.GlyphHome, () => onNavigateCurrentTab(homeUrl), "Go home", buttonSize: 32, iconSize: 15, useGlass: true),
                Button("Set home", onSetCurrentPageAsHome)
                    .AutomationName("Set current page as home")
                    .Height(32)
                    .Padding(10, 0)
                    .CornerRadius(16),
                IconButton(
                    selectedTab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                    onToggleFavorite,
                    "Toggle favorite",
                    buttonSize: 32,
                    iconSize: 15,
                    useGlass: true),
                BuildSearchProviderButton(selectedSearchProviderKey, searchProviders, onSelectSearchProvider),
                IconButton(
                    BrowserConstants.GlyphSettings,
                    onShowCommandCenterSettings,
                    "Command center settings",
                    buttonSize: 32,
                    iconSize: 15,
                    useGlass: true)
            ) with
            {
                ColumnGap = 8
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(8, 6, 8, 6)
        .Background(Theme.LayerFill)
        .WithBorder(Theme.SurfaceStroke)
        .HAlign(HorizontalAlignment.Stretch)
        .Flex(shrink: 0);
    }

    private static Element BuildAddressBar(
        BrowserTab selectedTab,
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
                    BuildAddressBarFavicon(selectedTab),
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
                .Padding(0, 0, 0, 2)
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
        .Padding(10, 1, 10, 0)
        .CornerRadius(14)
        .Background(BrowserConstants.LayerFillDefaultBrush)
        .HAlign(HorizontalAlignment.Stretch)
        .MinWidth(0)
        .Set(border => ConfigureAddressBarChrome(border, addressBarChrome = border));
    }

    private static Element BuildAddressBarFavicon(BrowserTab selectedTab)
    {
        return Border(
            Uri.TryCreate(selectedTab.Url, UriKind.Absolute, out _)
                ? Image(BrowserUrl.GetDomainFaviconUrl(selectedTab.Url))
                    .AccessibilityHidden()
                    .Width(16)
                    .Height(16)
                    .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
                : FluentIcon(BrowserConstants.GlyphHome, 14))
            .Width(24)
            .Height(24)
            .CornerRadius(8)
            .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
            .WithBorder(Theme.SurfaceStroke)
            .Padding(3)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .Flex(shrink: 0);
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
            state.Chrome.BorderThickness = new Thickness(0);
            state.Chrome.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        if (state.Underline is not null)
        {
            AnimateAddressBarUnderline(
                state.Underline,
                isFocused ? 1d : hasText ? 0.35d : 0d,
                isFocused ? 1d : hasText ? 0.82d : 0.6d);
        }
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
    IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
    IReadOnlyList<FavoriteItem> favoriteItems,
    string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        string commandCenterBusyText,
    IReadOnlyDictionary<string, string> settingsSnapshot,
    Action<string, string> onSaveSettingValue,
    Action<string> onHistoryFilterChanged,
    Action<string> onFavoritesFilterChanged,
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
        var collapsedCommandCenterHeight = CommandCenterCardHeight;

        if (!string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            collapsedCommandCenterHeight += CommandCenterBladeHeight + 4;
        }

        var railWidth = isTabsCollapsed ? CollapsedRailWidth : CollapsedRailWidthDefault ;

        var tabList = (ListView<BrowserTab>(
            tabs,
            (tab, _) =>
            {
                var isTabLoading = isLoading &&
                    string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);

                var isSelected = string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);

                return (isTabsCollapsed
                    ? BuildCollapsedTabItem(tab, isSelected, isTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab).Padding(0).CornerRadius(12)
                    : BuildExpandedTabItem(tab, isSelected, isTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab).Padding(4).CornerRadius(12)).WithKey($"{tab.Id}-{isTabsCollapsed}");
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
            listView.ContainerContentChanging -= OnTabListContainerContentChanging;
            listView.ContainerContentChanging += OnTabListContainerContentChanging;
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
                FlexColumn(
                    tabList
                        .Flex(grow: 1, basis: 0)
                )
            )
            .Padding(0)
            .Set(border => ConfigureRailContainer(border, railWidth, onRailTransitionCompleted))
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
                VStack(12,
                    BuildActiveTabHeader(selectedTab, tabs.Length, isSelectedTabLoading, onToggleFavoriteTab, onCloseTabFromContextMenu, onReloadTab),
                    BuildExpandableTabsList(tabList, onMinimizeTabs)
                        .Flex(grow: 1, shrink: 1, basis: 0)
                ).WithKey($"{selectedTab.Id}-expanded")
                .Flex(grow: 1, shrink: 1, basis: 0))
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
                    historyImportBrowserProfiles,
                    favoriteItems,
                    favoritesFilter,
                    favoritesImportStatus,
                    favoritesImportBrowserProfiles,
                    isCommandCenterBusy,
                     isCommandCenterHighlighted,
                    commandCenterBusyText,
                    settingsSnapshot,
                    onSaveSettingValue,
                    onHistoryFilterChanged,
                    onFavoritesFilterChanged,
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
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        string commandCenterBusyText,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action<string> onFavoritesFilterChanged,
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
            historyImportBrowserProfiles,
            favoriteItems,
            favoritesFilter,
            favoritesImportStatus,
            favoritesImportBrowserProfiles,
            isCommandCenterBusy,
            isCommandCenterHighlighted,
            settingsSnapshot,
            onSaveSettingValue,
            onHistoryFilterChanged,
            onFavoritesFilterChanged,
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
        IReadOnlyList<HistoryItem> mostVisitedItems,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        string historyFilter,
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isCommandCenterBusy,
        bool isCommandCenterHighlighted,
        IReadOnlyDictionary<string, string> settingsSnapshot,
        Action<string, string> onSaveSettingValue,
        Action<string> onHistoryFilterChanged,
        Action<string> onFavoritesFilterChanged,
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
        Action onToggleCommandCenterExpanded,
        Action onDismissCommandCenter)
    {
        var shouldHighlight = isCommandCenterBusy || isCommandCenterHighlighted;

        if (string.IsNullOrWhiteSpace(activeCommandCenterSection))
        {
            return Border(null).IsVisible(false);
        }

        Element content = activeCommandCenterSection switch
        {
            "History" =>  BuildHistoryBladeContent(settingsSnapshot, recentHistoryItems, historyFilter, historyImportStatus, historyImportBrowserProfiles, isCommandCenterBusy, onHistoryFilterChanged, onImportHistory, onImportBrowserHistory, onImportBrowserHistoryProfile, onDeleteAllHistory, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, isCommandCenterExpanded),
            "Recent" => BuildRecentBladeContent(settingsSnapshot, recentHistoryItems, isCommandCenterBusy, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, isCommandCenterExpanded),
            "MostVisited" => BuildMostVisitedBladeContent(settingsSnapshot, mostVisitedItems, isCommandCenterBusy, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, isCommandCenterExpanded),
            "Favorites" => BuildFavoritesBladeContent(settingsSnapshot, favoriteItems, favoritesFilter, favoritesImportStatus, favoritesImportBrowserProfiles, isCommandCenterBusy, onFavoritesFilterChanged, onImportFavorites, onImportBrowserFavorites, onImportBrowserFavoritesProfile, onDeleteAllFavorites, onOpenFavoriteItem, onOpenFavoriteItemInNewTab, onDeleteFavoriteItem, isCommandCenterExpanded),
            "Settings" => BuildSettingsBladeContent(settingsSnapshot, onSaveSettingValue),
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
                        iconSize: 8,
                        useGlass: true)
                    .Margin(2, 0, 2, 0),
                    IconButton(
                        BrowserConstants.GlyphClose,
                        onDismissCommandCenter,
                        "Dismiss command center blade",
                        buttonSize: 24,
                        iconSize: 8,
                        useGlass: true)
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
        .Padding(2)
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

        return Border(
            Button(label ?? section, () => onToggleCommandCenter(section))
                .CornerRadius(10)
                .Padding(6)
                .Flex(grow: 1, basis: 0)
                .AutomationName(label ?? section)
                .Set(button =>
                {
                    button.Style = GetGlassIconButtonStyle();
                    button.Background = isActive
                        ? BrowserConstants.LayerFillAltBrush
                        : BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush;
                    button.BorderBrush = isActive
                        ? BrowserConstants.SubtleFillColorSecondaryBrush
                        : BrowserConstants.SurfaceStrokeColorDefaultBrush;
                    button.BorderThickness = new Thickness(isActive ? 1.5 : 1);
                })
        ).WithKey($"cc-button-{ label}-{section}")
        .CornerRadius(10)
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

    private static Element BuildCompactTabsCard(
        BrowserTab tab,
        Action onShowTabs,
        Action<string> onToggleFavoriteTab,
        bool isLoading)
    {
        return Border(
            Border(
                (FlexRow(
                    BuildTabIcon(tab, isLoading),
                    TextBlock(tab.Title)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .TextWrapping(TextWrapping.Wrap)
                        .Set(textBlock =>
                        {
                            textBlock.FontFamily = BrowserConstants.TextFontFamily;
                            textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                            textBlock.MaxLines = 2;
                            textBlock.MinWidth = 0;
                        }).FontSize(12)
                        .MinWidth(0)
                        .Flex(grow: 1, basis: 0),
                    IconButton(
                        tab.IsFavorite ? BrowserConstants.GlyphFavorite : BrowserConstants.GlyphFavoriteOutline,
                        () => onToggleFavoriteTab(tab.Id),
                        tab.IsFavorite ? "Remove active tab from favorites" : "Add active tab to favorites",
                        buttonSize: 28,
                        iconSize: 14)
                        .Flex(shrink: 0)
                ) with
                {
                    ColumnGap = 8
                })
                .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(10, 8)
            .CornerRadius(12)
            .Set(border => border.Style = GetGlassCardStyle())
        )
        .Padding(12)
        .CornerRadius(16)
        .Set(border =>
        {
            border.Style = GetGlassCardStyle();
            border.DoubleTapped += (_, _) => onShowTabs();
            ToolTipService.SetToolTip(border, "Double-click to show the full tabs list.");
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
        border.BorderBrush = BrowserConstants.SurfaceStrokeColorDefaultBrush;
    }

    private static Element BuildExpandableTabsList(Element tabList, Action onMinimizeTabs)
    {
        return Border(
            tabList
                .Flex(grow: 1, shrink: 1, basis: 0)
                .VAlign(VerticalAlignment.Stretch))
            .Padding(2, 2, 8, 2)
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
                        iconSize: 14)
                ) with
                {
                    ColumnGap = 8
                })
                .HAlign(HorizontalAlignment.Stretch),
                Border(
                    VStack(10,
                        (FlexRow(
                            BuildTabIcon(tab, isLoading),
                            VStack(4,
                                TextBlock(tab.Title)
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Set(textBlock =>
                                    {
                                        textBlock.FontFamily = BrowserConstants.TextFontFamily;
                                        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                                        textBlock.MaxLines = 2;
                                        textBlock.MinWidth = 0;
                                    }),
                                TextBlock("Active tab")
                                    .Opacity(0.66)
                                    .FontSize(11)
                            )
                            .MinWidth(0)
                            .Flex(grow: 1, basis: 0),
                            HStack(4,
                                IconButton(BrowserConstants.GlyphRefresh, () => onReloadTab(tab.Id), "Reload active tab", buttonSize: 28, iconSize: 13),
                                IconButton(BrowserConstants.GlyphClose, () => onCloseTab(tab.Id), "Close active tab", buttonSize: 28, iconSize: 13)
                            ).Flex(shrink: 0)
                        ) with
                        {
                            ColumnGap = 10
                        })
                        .HAlign(HorizontalAlignment.Stretch),
                        HStack(8,
                            BuildTabMetricPill("Session", FormatTabSessionAge(tab.DateTime)),
                            BuildTabMetricPill("Opened", tab.DateTime.ToString("g")),
                            BuildTabMetricPill("Visits", $"{Math.Max(tab.VisitedCount, 0)}")
                        )
                    )
                    .HAlign(HorizontalAlignment.Stretch)
                )
                .Padding(12)
                .CornerRadius(12)
                .Background(BrowserConstants.LayerFillDefaultBrush)
                .WithBorder(Theme.SurfaceStroke)
            )
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(12)
        .CornerRadius(16)
        .Background(BrowserConstants.LayerFillAltBrush)
        .WithBorder(Theme.SurfaceStroke)
        .MinHeight(ActiveTabHeaderMinHeight)
        .HAlign(HorizontalAlignment.Stretch);
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
        .CornerRadius(10)
        .Background(BrowserConstants.SubtleFillColorSecondaryBrush)
        .WithBorder(Theme.SurfaceStroke)
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
        string historyImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyImportBrowserProfiles,
        bool isImportRunning,
        Action<string> onHistoryFilterChanged,
        Action onImportHistory,
        Action<string> onImportBrowserHistory,
        Action<string, string> onImportBrowserHistoryProfile,
        Action onDeleteAllHistory,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var historyItems = BuildGroupedHistoryItems(
            recentHistoryItems,
            item => BuildHistoryListItem(item, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, openInNewTabByDefault))
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
                        .Set(button => button.Flyout = CreateBrowserImportFlyout(
                            historyImportBrowserProfiles,
                            onImportHistory,
                            onImportBrowserHistory,
                            onImportBrowserHistoryProfile,
                            isImportRunning)),
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
            isImportRunning
                ? BuildCommandCenterLoadingState(
                    "Gathering history items…",
                    BuildCommandCenterLoadingRows(4).ToArray())
                : historyItems.Length == 0
                ? Border(
                    TextBlock("No history items.")
                        .Opacity(0.7)
                )
                .Padding(8, 4)
                : Border(
                    VStack(0, historyItems)
                        .HAlign(HorizontalAlignment.Stretch)
                )
                .Padding(4, 0)
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0));
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
        .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element BuildRecentBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<HistoryItem> recentHistoryItems,
        bool isLoading,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var recentItems = recentHistoryItems
            .Select(item => BuildHistoryListItem(item, onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, openInNewTabByDefault))
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
                    VStack(0, recentItems)
                        .HAlign(HorizontalAlignment.Stretch)
                )
                .Padding(4, 0)
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0));
    }

    private static Element BuildMostVisitedBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<HistoryItem> mostVisitedItems,
        bool isLoading,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var topItems = mostVisitedItems.ToArray();
        var rows = new List<Element>();

        for (var index = 0; index < topItems.Length; index += 2)
        {
            var cards = new List<Element>();

            for (var column = index; column < Math.Min(index + 2, topItems.Length); column++)
            {
                cards.Add(BuildMostVisitedItem(topItems[column], onOpenHistoryItem, onOpenHistoryItemInNewTab, onDeleteHistoryItem, openInNewTabByDefault)
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
                : VStack(10, rows.ToArray())
                    .Padding(0, 0, 6, 0)).VAlign(VerticalAlignment.Center);
    }

    private static Element BuildFavoritesBladeContent(
        IReadOnlyDictionary<string, string> settingsSnapshot,
        IReadOnlyList<FavoriteItem> favoriteItems,
        string favoritesFilter,
        string favoritesImportStatus,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoritesImportBrowserProfiles,
        bool isImportRunning,
        Action<string> onFavoritesFilterChanged,
        Action onImportFavorites,
        Action<string> onImportBrowserFavorites,
        Action<string, string> onImportBrowserFavoritesProfile,
        Action onDeleteAllFavorites,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
        bool isCommandCenterExpanded)
    {
        var openInNewTabByDefault = GetBooleanSetting(settingsSnapshot, BrowserConstants.FavoritesOpenInNewTabSettingKey);
        var favoriteRows = new List<Element>();

        for (var index = 0; index < favoriteItems.Count; index++)
        {
            favoriteRows.Add(BuildFavoriteTabItem(favoriteItems[index], onOpenFavoriteItem, onOpenFavoriteItemInNewTab, onDeleteFavoriteItem, openInNewTabByDefault));
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
                        .Set(button => button.Flyout = CreateBrowserImportFlyout(
                            favoritesImportBrowserProfiles,
                            onImportFavorites,
                            onImportBrowserFavorites,
                            onImportBrowserFavoritesProfile,
                            isImportRunning)),
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
            isImportRunning
                ? BuildCommandCenterLoadingState(
                    "Gathering favorite items…",
                    BuildCommandCenterLoadingRows(4).ToArray())
                : favoriteItems.Count == 0
                ? Border(
                    TextBlock("No favorites yet. Star a tab or import bookmarks from another browser.")
                        .Opacity(0.7)
                        .TextWrapping(TextWrapping.Wrap)
                )
                .Padding(8, 4)
                : Border(
                    VStack(6, favoriteRows.ToArray())
                        .HAlign(HorizontalAlignment.Stretch)
                )
                .Padding(4, 0)
                .HAlign(HorizontalAlignment.Stretch)
                .MinWidth(0));
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
        var historyOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.HistoryOpenInNewTabSettingKey);
        var favoritesOpenInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.FavoritesOpenInNewTabSettingKey);
        var addressBarOpenDifferentDomainInNewTab = GetBooleanSetting(settingsSnapshot, BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey);

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
                    .Background(value ? BrowserConstants.AccentFillColorTertiaryBrush : BrowserConstants.LayerFillDefaultBrush)
                    .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White))
                    .CornerRadius(999)
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

    private static bool GetBooleanSetting(IReadOnlyDictionary<string, string> settingsSnapshot, string key)
    {
        return settingsSnapshot.TryGetValue(key, out var value) &&
            bool.TryParse(value, out var enabled) &&
            enabled;
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
            .Background(isSelected ? BrowserConstants.AccentFillColorTertiaryBrush : BrowserConstants.LayerFillDefaultBrush)
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
            ? new Thickness(TabItemHorizontalInset, 6, TabItemHorizontalInset, 6)
            : new Thickness(TabItemHorizontalInset, 6, TabItemHorizontalInset, 6);

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
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        return Border(
            BuildTabIcon(tab, isLoading, useTileChrome: false).CornerRadius(10).WithKey("CollapsedTabIcon" + tab.Id)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center).CornerRadius(8)
        )
        .Width(CollapsedTabItemHeight)
        .Height(CollapsedTabItemHeight)
        .Padding()
        .Set(border => border.Style = GetCollapsedTabGlassCardStyle())
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, onToggleFavoriteTab, onCloseTab, onReloadTab);
            ToolTipService.SetToolTip(border, CreateTabToolTip(tab));
            ApplyTabItemBorderState(border, isSelected, IsTabCreationLoading(tab, isLoading));
        });
    }



    private static Element BuildExpandedTabItem(
        BrowserTab tab,
        bool isSelected,
        bool isLoading,
        Action<string> onToggleFavoriteTab,
        Action<string> onCloseTab,
        Action<string> onReloadTab)
    {
        return Border(
            (FlexRow(
                BuildTabIcon(tab, isLoading),
                Border(
                    TextBlock(tab.Title)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .TextWrapping(TextWrapping.Wrap)
                        .Set(textBlock =>
                        {
                            textBlock.FontFamily = BrowserConstants.TextFontFamily;
                            textBlock.MaxLines = 2;
                            textBlock.MinHeight = 34;
                            textBlock.MinWidth = 0;
                        })
                )
                .MinWidth(0)
                .Flex(grow: 1, basis: 0),
                Border(
                    InfoBadge(tab.VisitedCount)
                        .HAlign(HorizontalAlignment.Right)
                        .VAlign(VerticalAlignment.Center)
                )
                .Width(26)
                .HAlign(HorizontalAlignment.Right)
                .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 10
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Height(ExpandedTabItemHeight)
        .Padding(12, 10)
        .CornerRadius(10)
        .Set(border => border.Style = GetGlassCardStyle())
        .HAlign(HorizontalAlignment.Stretch)
        .Set(border =>
        {
            border.ContextFlyout = CreateTabContextFlyout(tab, onToggleFavoriteTab, onCloseTab, onReloadTab);
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

            border.BorderThickness = isSelected ? new Thickness(SelectedTabBorderThickness) : new Thickness(0);
            border.BorderBrush = isSelected
                ? BrowserConstants.AccentFillColorTertiaryBrush
                : null;
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
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1.4)),
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
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
        bool openInNewTabByDefault)
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
            () => OpenItem(item.Url, openInNewTabByDefault, onOpenHistoryItem, onOpenHistoryItemInNewTab))
            .Set(button => button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, onOpenHistoryItem, onOpenHistoryItemInNewTab, () => onDeleteHistoryItem(item.Url), "Delete history item"))
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

    private static Element BuildHistoryListItem(
        HistoryItem item,
        Action<string> onOpenHistoryItem,
        Action<string> onOpenHistoryItemInNewTab,
        Action<string> onDeleteHistoryItem,
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
                                })
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
                        button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, onOpenHistoryItem, onOpenHistoryItemInNewTab, () => onDeleteHistoryItem(item.Url), "Delete history item");
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
        .AutomationName("HistoryListItem");
    }

    private static Element BuildFavoriteTabItem(
        FavoriteItem item,
        Action<string> onOpenFavoriteItem,
        Action<string> onOpenFavoriteItemInNewTab,
        Action<string> onDeleteFavoriteItem,
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
                                })
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
                        button.ContextFlyout = CreateOpenItemContextFlyout(item.Url, onOpenFavoriteItem, onOpenFavoriteItemInNewTab, () => onDeleteFavoriteItem(item.Id), "Remove favorite");
                    }),
                IconButton(BrowserConstants.GlyphClose, () => onDeleteFavoriteItem(item.Id), "Remove favorite", buttonSize: 24, iconSize: 10, useGlass: true)
                    .Flex(shrink: 0)
            ) with
            {
                ColumnGap = 8
            })
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Padding(8, 6)
        .CornerRadius(8)
        .Margin(2, 0)
        .HAlign(HorizontalAlignment.Stretch)
        .AutomationName("FavoriteItem");
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
        string? deleteText = null)
    {
        var flyout = new MenuFlyout();

        var openItem = new MenuFlyoutItem
        {
            Text = "Open"
        };
        openItem.Click += (_, _) => onOpenCurrentTab(url);
        flyout.Items.Add(openItem);

        var openInNewTabItem = new MenuFlyoutItem
        {
            Text = "Open in new tab"
        };
        openInNewTabItem.Click += (_, _) => onOpenNewTab(url);
        flyout.Items.Add(openInNewTabItem);

        if (onDeleteItem is not null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem
            {
                Text = string.IsNullOrWhiteSpace(deleteText) ? "Delete" : deleteText
            };
            deleteItem.Click += (_, _) => onDeleteItem();
            flyout.Items.Add(deleteItem);
        }

        return flyout;
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
                .Width(16)
                .Height(16)
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
        var iconContent = Uri.TryCreate(tab.Url, UriKind.Absolute, out _)
            ? Image(BrowserUrl.GetDomainFaviconUrl(tab.Url))
                .AccessibilityHidden()
                .Width(useTileChrome ? 16 : 18)
                .Height(useTileChrome ? 16 : 18)
                .Set(image => image.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill)
            : FluentIcon(BrowserConstants.GlyphHome, useTileChrome ? 14 : 16);

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
        double iconSize = 14,
        bool useGlass = false)
    {
        return Button(FluentIcon(glyph, iconSize), onClick)
            .AutomationName(automationName)
            .Width(buttonSize)
            .Height(buttonSize)
            .Padding(0)
            .Set(button =>
            {
                if (useGlass)
                {
                    button.Style = GetGlassIconButtonStyle();
                }
            });
    }

    private static Style GetGlassIconButtonStyle()
    {
        return _glassIconButtonStyle ??= new Style(typeof(Microsoft.UI.Xaml.Controls.Button))
        {
            Setters =
            {
                new Setter(Microsoft.UI.Xaml.Controls.Control.BackgroundProperty, BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Control.BorderBrushProperty, BrowserConstants.SurfaceStrokeColorDefaultBrush),
                new Setter(Microsoft.UI.Xaml.Controls.Control.BorderThicknessProperty, new Thickness(1)),
                new Setter(Microsoft.UI.Xaml.Controls.Control.CornerRadiusProperty, new CornerRadius(10))
            }
        };
    }

    private static Style GetGlassCardStyle()
    {
        return _glassCardStyle ??= new Style(typeof(Microsoft.UI.Xaml.Controls.Border))
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
        return _collapsedTabGlassCardStyle ??= new Style(typeof(Microsoft.UI.Xaml.Controls.Border))
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
                }
            }
        };
    }
}