using LinkScape.Browser;
using LinkScape.Browser.State;
using LinkScape.Models;
using Browser.Components;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape;

class TabViewPage : Component
{
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";

    private enum CommandCenterSection
    {
        None,
        History,
        Recent,
        MostVisited,
        Settings,
        Backdrop,
        Favorites,
        Chat
    }

    private const string PauseMediaScript = """
        (() => {
            const media = Array.from(document.querySelectorAll('video, audio'));

            for (const element of media) {
                if (typeof element.pause === 'function') {
                    element.pause();
                }
            }

            return media.length;
        })();
        """;

    private CancellationTokenSource? _saveTabsCts;
    private string? _latestSelectedTabId;
    private bool _shutdownSaveRegistered;
    private const int MaxTabs = 50;
    private const int MaxTitleLength = 256;
    private const int MaxUrlLength = 2048;
    private readonly Dictionary<string, Microsoft.UI.Xaml.Controls.WebView2> _webViewsByTabId = [];
    private readonly HashSet<string> _hookedWebViewTabs = [];
    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private Microsoft.UI.Xaml.Controls.Border? _webViewHost;
    private string? _activeWebViewTabId;
    private Action<string, string>? _setTitleFromCore;
    private Action<bool, bool>? _setNavAvailability;
    private Action<string>? _setAddressFromCore;
    private Action<bool>? _setLoadingStateFromCore;
    private Action? _refreshHistoryFromCore;
    private bool _importBrowserNamesLoadStarted;
    private int _commandCenterBusyVersion;
    private BrowserTab[] _latestTabs = [];
    private readonly BrowserTab[] _startupTabs;
    private readonly string _startupSelectedTabId;

    public TabViewPage()
    {
        _startupTabs = LoadStartupTabs();

        var persistedSelectedTabId = TabPersistenceService.LoadTabs<string>("selectedTabId");
        var selectedTab = _startupTabs.FirstOrDefault(tab => tab.Id == persistedSelectedTabId) ?? _startupTabs[0];

        _startupSelectedTabId = selectedTab.Id;
        _latestTabs = _startupTabs;
        _latestSelectedTabId = _startupSelectedTabId;

        RegisterShutdownSave();
    }

    public override Element Render()
    {
        var startupSelectedTab = _startupTabs.FirstOrDefault(tab => tab.Id == _startupSelectedTabId) ?? _startupTabs[0];
        var (tabs, setTabs) = UseState(_startupTabs);
        _latestTabs = tabs;
        var (selectedTag, setSelectedTag) = UseState(_startupSelectedTabId);
        _latestSelectedTabId = selectedTag;
        var (addressText, setAddressText) = UseState(startupSelectedTab.Url);
        var (isTabsCollapsed, setIsTabsCollapsed) = UseState(true);
        var (canGoBack, setCanGoBack) = UseState(false);
        var (canGoForward, setCanGoForward) = UseState(false);
        var (isLoading, setIsLoading) = UseState(false);
        var (historyFilter, setHistoryFilter) = UseState(string.Empty);
        var (recentHistory, setRecentHistory) = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var (mostVisitedHistory, setMostVisitedHistory) = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var (favoritesFilter, setFavoritesFilter) = UseState(string.Empty);
        var (favoriteItems, setFavoriteItems) = UseState(Array.Empty<FavoriteItem>(), threadSafe: true);
        var (favoritesImportStatus, setFavoritesImportStatus) = UseState(string.Empty, threadSafe: true);
        var (historyImportStatus, setHistoryImportStatus) = UseState(string.Empty, threadSafe: true);
        var (isCommandCenterBusy, setIsCommandCenterBusy) = UseState(false, threadSafe: true);
        var (commandCenterBusyText, setCommandCenterBusyText) = UseState(string.Empty, threadSafe: true);
        var (historyImportBrowserNames, setHistoryImportBrowserNames) = UseState(Array.Empty<string>(), threadSafe: true);
        var (favoritesImportBrowserNames, setFavoritesImportBrowserNames) = UseState(Array.Empty<string>(), threadSafe: true);
        var (activeCommandCenterSection, setActiveCommandCenterSection) = UseState(CommandCenterSection.None);
        var (isCommandCenterExpanded, setIsCommandCenterExpanded) = UseState(false);
        var (isRailTabsExpanded, setIsRailTabsExpanded) = UseState(true);
        var (settingsSnapshot, setSettingsSnapshot) = UseState<IReadOnlyDictionary<string, string>>(SettingsService.Dump());
        var (selectedSearchProviderKey, setSelectedSearchProviderKey) = UseState(
            BrowserSearchProviders.NormalizeProviderKey(
                SettingsService.GetValueOrDefault(
                    DefaultSearchProviderSettingKey,
                    BrowserSearchProviders.DefaultProviderKey)));

        var isCommandCenterOpen = activeCommandCenterSection != CommandCenterSection.None;

        if (!_importBrowserNamesLoadStarted)
        {
            _importBrowserNamesLoadStarted = true;
            _ = Task.Run(() =>
            {
                setHistoryImportBrowserNames(GetHistoryImportBrowserNames());
                setFavoritesImportBrowserNames(GetFavoritesImportBrowserNames());
            });
        }

        void MarkTabsChanged(BrowserTab[] nextTabs)
        {
            _latestTabs = nextTabs;
            setTabs(nextTabs);
            ScheduleTabsSave(nextTabs, selectedTag);
        }

        void ToggleCommandCenter(CommandCenterSection section)
        {
            if (activeCommandCenterSection == section)
            {
                setIsCommandCenterExpanded(false);
            }

            var nextSection = activeCommandCenterSection == section
                ? CommandCenterSection.None
                : section;

            setActiveCommandCenterSection(nextSection);

            switch (nextSection)
            {
                case CommandCenterSection.History:
                case CommandCenterSection.Recent:
                case CommandCenterSection.MostVisited:
                    RefreshHistoryState();
                    break;
                case CommandCenterSection.Favorites:
                    RefreshFavoritesState();
                    break;
            }
        }

        void DismissCommandCenter()
        {
            if (isCommandCenterExpanded)
            {
                setIsCommandCenterExpanded(false);
                return;
            }

            if (isCommandCenterOpen)
            {
                setActiveCommandCenterSection(CommandCenterSection.None);
            }
        }

        void ToggleCommandCenterExpanded()
        {
            if (!isCommandCenterOpen)
            {
                return;
            }

            var nextExpanded = !isCommandCenterExpanded;
            setIsCommandCenterExpanded(nextExpanded);

            if (nextExpanded)
            {
                setIsRailTabsExpanded(false);
            }
        }

        void MaximizeRailTabsCard()
        {
            setIsCommandCenterExpanded(false);
            setActiveCommandCenterSection(CommandCenterSection.None);
            setIsRailTabsExpanded(true);
        }

        void MinimizeRailTabsCard()
        {
            setIsCommandCenterExpanded(false);
            setActiveCommandCenterSection(CommandCenterSection.None);
            setIsRailTabsExpanded(false);
        }

        void ToggleCommandCenterByName(string sectionName)
        {
            if (!Enum.TryParse<CommandCenterSection>(sectionName, ignoreCase: false, out var section))
            {
                section = CommandCenterSection.None;
            }

            ToggleCommandCenter(section);
        }

        void SetDefaultSearchProvider(string providerKey)
        {
            var normalizedProviderKey = BrowserSearchProviders.NormalizeProviderKey(providerKey);
            SettingsService.SetValue(DefaultSearchProviderSettingKey, normalizedProviderKey);
            setSelectedSearchProviderKey(normalizedProviderKey);
            setSettingsSnapshot(SettingsService.Dump());
        }

        void SaveSettingValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            SettingsService.SetValue(key, value);
            setSettingsSnapshot(SettingsService.Dump());

            if (string.Equals(key, DefaultSearchProviderSettingKey, StringComparison.Ordinal))
            {
                setSelectedSearchProviderKey(BrowserSearchProviders.NormalizeProviderKey(value));
            }
        }

        void OpenUriInNewTab(string rawUrl)
        {
            DismissCommandCenter();

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;

            if (currentTabs.Length >= MaxTabs)
            {
                return;
            }

            var target = BrowserUrl.Normalize(rawUrl, BrowserConstants.HomeUrl, selectedSearchProviderKey);
            var nextTabs = BrowserTabActions.Add(
                currentTabs,
                target,
                out var newTab,
                visitCount: 1);

            MarkTabsChanged(nextTabs);

            setSelectedTag(newTab.Id);
            setAddressText(newTab.Url);

            _activeWebViewTabId = newTab.Id;
            ScheduleTabsSave(nextTabs, newTab.Id);
        }

        void UpdateTab(string id, Func<BrowserTab, BrowserTab> updater)
        {
            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
            var nextTabs = BrowserTabActions.Replace(currentTabs, id, updater, out var changed);

            if (changed)
            {
                MarkTabsChanged(nextTabs);
            }
        }

        void SetAddressIfNeeded(string nextAddress)
        {
            if (!string.Equals(addressText, nextAddress, StringComparison.Ordinal))
            {
                setAddressText(nextAddress);
            }
        }

        void SetNavAvailabilityIfNeeded(bool back, bool forward)
        {
            if (canGoBack != back)
            {
                setCanGoBack(back);
            }

            if (canGoForward != forward)
            {
                setCanGoForward(forward);
            }
        }

        void SetLoadingIfNeeded(bool next)
        {
            if (isLoading != next)
            {
                setIsLoading(next);
            }
        }

        int BeginCommandCenterWork(string busyText)
        {
            var version = Interlocked.Increment(ref _commandCenterBusyVersion);
            setIsCommandCenterBusy(true);
            setCommandCenterBusyText(busyText);
            return version;
        }

        void EndCommandCenterWork(int version)
        {
            if (version != Volatile.Read(ref _commandCenterBusyVersion))
            {
                return;
            }

            setCommandCenterBusyText(string.Empty);
            setIsCommandCenterBusy(false);
        }

        void SetHistoryStateFromDatabase(string? filterOverride = null)
        {
            var effectiveFilter = filterOverride ?? historyFilter;
            setRecentHistory(LoadRecentHistoryItems(effectiveFilter));
            setMostVisitedHistory(LoadMostVisitedHistoryItems());
        }

        void RefreshHistoryState(string? filterOverride = null)
        {
            var version = BeginCommandCenterWork("Loading history…");

            _ = Task.Run(() =>
            {
                try
                {
                    SetHistoryStateFromDatabase(filterOverride);
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ApplyHistoryFilter(string nextFilter)
        {
            setHistoryFilter(nextFilter);
            setRecentHistory(LoadRecentHistoryItems(nextFilter));
        }

        void SetFavoritesStateFromDatabase(string? filterOverride = null)
        {
            var effectiveFilter = filterOverride ?? favoritesFilter;
            setFavoriteItems(LoadFavoriteItems(effectiveFilter));
        }

        void RefreshFavoritesState(string? filterOverride = null)
        {
            var version = BeginCommandCenterWork("Loading favorites…");

            _ = Task.Run(() =>
            {
                try
                {
                    SetFavoritesStateFromDatabase(filterOverride);
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ApplyFavoritesFilter(string nextFilter)
        {
            setFavoritesFilter(nextFilter);
            setFavoriteItems(LoadFavoriteItems(nextFilter));
        }

        void ImportBrowserHistory()
        {
            if (isCommandCenterBusy)
            {
                return;
            }

            setActiveCommandCenterSection(CommandCenterSection.History);
            var version = BeginCommandCenterWork("Importing history…");
            setHistoryImportStatus("Importing browser history…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserHistoryImportService.ImportAllHistory();
                    setHistoryImportStatus(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} items from {summary.SourceCount} sources"
                        : "No supported browser history sources were found.");
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    setHistoryImportStatus("Browser history import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ImportBrowserHistoryByName(string browserName)
        {
            if (isCommandCenterBusy)
            {
                return;
            }

            setActiveCommandCenterSection(CommandCenterSection.History);
            var version = BeginCommandCenterWork($"Importing {browserName} history…");
            setHistoryImportStatus($"Importing {browserName} history…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserHistoryImportService.ImportBrowserHistory(browserName);
                    setHistoryImportStatus(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} items from {browserName}"
                        : $"No {browserName} history was imported.");
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    setHistoryImportStatus($"{browserName} history import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void DeleteAllHistory()
        {
            HistoryPersistenceService.ClearHistory();
            setHistoryImportStatus("Deleted all history.");
            RefreshHistoryState();
        }

        void ImportBrowserFavorites()
        {
            if (isCommandCenterBusy)
            {
                return;
            }

            setActiveCommandCenterSection(CommandCenterSection.Favorites);
            var version = BeginCommandCenterWork("Importing favorites…");
            setFavoritesImportStatus("Importing browser favorites…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserFavoritesImportService.ImportAllFavorites();
                    setFavoritesImportStatus(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} favorites from {summary.SourceCount} sources"
                        : "No supported browser favorites were found.");
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    setFavoritesImportStatus("Browser favorites import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ImportBrowserFavoritesByName(string browserName)
        {
            if (isCommandCenterBusy)
            {
                return;
            }

            setActiveCommandCenterSection(CommandCenterSection.Favorites);
            var version = BeginCommandCenterWork($"Importing {browserName} favorites…");
            setFavoritesImportStatus($"Importing {browserName} favorites…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserFavoritesImportService.ImportBrowserFavorites(browserName);
                    setFavoritesImportStatus(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} favorites from {browserName}"
                        : $"No {browserName} favorites were imported.");
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    setFavoritesImportStatus($"{browserName} favorites import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void DeleteAllFavorites()
        {
            FavoritesService.ClearFavorites();
            var nextTabs = tabs
                .Select(tab => tab with
                {
                    FavoriteId = string.Empty,
                    IsFavorite = false
                })
                .ToArray();

            setFavoritesImportStatus("Deleted all favorites.");
            MarkTabsChanged(nextTabs);
            RefreshFavoritesState();
        }

        void OpenHistoryItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            DismissCommandCenter();
            NavigateActiveTab(url);
        }

        void ApplyWebViewSource(string tabId, string url)
        {
            if (!_webViewsByTabId.TryGetValue(tabId, out var webView))
            {
                return;
            }

            try
            {
                if (webView.CoreWebView2 is not null)
                {
                    webView.CoreWebView2.Navigate(url);
                }
                else
                {
                    webView.Source = new Uri(url);
                }

                SetLoadingIfNeeded(true);
            }
            catch
            {
                SetLoadingIfNeeded(false);
            }
        }

        void NavigateActiveTab(string rawUrl)
        {
            var activeId = selectedTag;
            var fallback = tabs.FirstOrDefault(tab => tab.Id == activeId)?.Url ?? BrowserConstants.HomeUrl;
            var target = BrowserUrl.Normalize(rawUrl, fallback, selectedSearchProviderKey);

            SetAddressIfNeeded(target);

            var previousUrl = tabs.FirstOrDefault(tab => tab.Id == activeId)?.Url;
            var urlChanged = !BrowserUrl.AreEqual(previousUrl, target);

            UpdateTab(activeId, tab =>
            {
                if (BrowserUrl.AreEqual(tab.Url, target))
                {
                    return tab;
                }

                return tab with
                {
                    Url = target,
                    DateTime = DateTime.Now
                };
            });

            try
            {
                TabPersistenceService.UpdateTabVisit(
                    "tabs",
                    activeId,
                    incrementVisitCount: true,
                    newUrl: target,
                    urlChanged: urlChanged);
            }
            catch
            {
            }

            ApplyWebViewSource(activeId, target);
        }

        void SubmitAddress(string rawUrl)
        {
            var currentUrl = tabs.FirstOrDefault(tab => tab.Id == selectedTag)?.Url;
            var fallback = currentUrl ?? BrowserConstants.HomeUrl;
            var target = BrowserUrl.Normalize(rawUrl, fallback, selectedSearchProviderKey);

            if (!BrowserUrl.AreEqual(currentUrl, target))
            {
                OpenUriInNewTab(target);
                return;
            }

            NavigateActiveTab(target);
        }

        void AddTab()
        {
            setIsCommandCenterExpanded(false);
            setActiveCommandCenterSection(CommandCenterSection.None);
            setIsRailTabsExpanded(true);

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;

            if (currentTabs.Length >= MaxTabs)
            {
                return;
            }

            var nextTabs = BrowserTabActions.Add(
                currentTabs,
                BrowserConstants.HomeUrl,
                out var newTab);

            MarkTabsChanged(nextTabs);

            setSelectedTag(newTab.Id);
            setAddressText(newTab.Url);

            _activeWebViewTabId = newTab.Id;

            ScheduleTabsSave(nextTabs, newTab.Id);

            try
            {
                var node = JsonSerializer.SerializeToNode(newTab) as JsonObject;

                if (node is not null)
                {
                    TabPersistenceService.SaveOrReplaceTabJson("tabs", node);
                }
            }
            catch
            {
            }
        }

        void CloseTab(string tabId)
        {
            DismissCommandCenter();

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;

            if (currentTabs.Length <= 1)
            {
                return;
            }

            var index = Array.FindIndex(currentTabs, tab => tab.Id == tabId);

            if (index < 0)
            {
                return;
            }

            var wasSelected = string.Equals(selectedTag, tabId, StringComparison.Ordinal);
            var nextTabs = BrowserTabActions.Close(currentTabs, tabId, out var nextTab);

            if (nextTab is null)
            {
                return;
            }

            if (_webViewsByTabId.Remove(tabId, out var closedWebView))
            {
                if (_webViewHost?.Child == closedWebView)
                {
                    _webViewHost.Child = null;
                }

                closedWebView.Close();
            }

            _hookedWebViewTabs.Remove(tabId);
            MarkTabsChanged(nextTabs);

            if (wasSelected)
            {
                setSelectedTag(nextTab.Id);
                setAddressText(nextTab.Url);
                _activeWebViewTabId = nextTab.Id;
                ScheduleTabsSave(nextTabs, nextTab.Id);
                return;
            }

            ScheduleTabsSave(nextTabs, selectedTag);
        }

        void CloseActiveTab()
        {
            CloseTab(selectedTag);
        }

        void ToggleFavoriteTab(string tabId)
        {
            var targetTab = (_latestTabs.Length > 0 ? _latestTabs : tabs)
                .FirstOrDefault(tab => tab.Id == tabId);

            if (targetTab is null)
            {
                return;
            }

            if (targetTab.IsFavorite)
            {
                try
                {
                    FavoritesService.RemoveFavorite(targetTab.FavoriteId);
                }
                catch
                {
                }

                UpdateTab(targetTab.Id, tab => tab with
                {
                    IsFavorite = false,
                    FavoriteId = string.Empty,
                    DateTime = DateTime.Now
                });

                RefreshFavoritesState();

                return;
            }

            try
            {
                var favorite = FavoritesService.UpsertFavorite(targetTab.FavoriteId, targetTab.Url, targetTab.Title);

                UpdateTab(targetTab.Id, tab => tab with
                {
                    IsFavorite = true,
                    FavoriteId = favorite.Id,
                    DateTime = DateTime.Now
                });

                RefreshFavoritesState();
            }
            catch
            {
            }
        }

        void ToggleFavorite()
        {
            ToggleFavoriteTab(selectedTag);
        }

        void ReloadTab(string tabId)
        {
            if (_webViewsByTabId.TryGetValue(tabId, out var webView))
            {
                if (webView.CoreWebView2 is not null)
                {
                    webView.CoreWebView2.Reload();
                    return;
                }

                var url = (_latestTabs.Length > 0 ? _latestTabs : tabs)
                    .FirstOrDefault(tab => tab.Id == tabId)?.Url;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    ApplyWebViewSource(tabId, url);
                }
            }
        }

        void SelectTab(int index)
        {
            DismissCommandCenter();

            if (index < 0 || index >= tabs.Length)
            {
                return;
            }

            var previousTabId = selectedTag;
            var nextTab = tabs[index];

            if (!string.Equals(previousTabId, nextTab.Id, StringComparison.Ordinal))
            {
                _ = CaptureScrollPositionAsync(previousTabId, UpdateTab);
                _ = PauseMediaInTabAsync(previousTabId);

                setSelectedTag(nextTab.Id);
                ScheduleTabsSave(_latestTabs.Length > 0 ? _latestTabs : tabs, nextTab.Id);

                try
                {
                    TabPersistenceService.UpdateTabVisit(
                        "tabs",
                        nextTab.Id,
                        incrementVisitCount: true,
                        newUrl: nextTab.Url,
                        urlChanged: false);
                }
                catch
                {
                }
            }

            SetAddressIfNeeded(nextTab.Url);

            _activeWebViewTabId = nextTab.Id;

            if (_webViewsByTabId.TryGetValue(nextTab.Id, out var selectedWebView))
            {
                _webView = selectedWebView;

                if (selectedWebView.CoreWebView2 is { } core)
                {
                    SetNavAvailabilityIfNeeded(core.CanGoBack, core.CanGoForward);
                }
            }
        }

        var selectedTab = tabs.FirstOrDefault(tab => tab.Id == selectedTag) ?? tabs[0];

        _setTitleFromCore = (tabId, title) =>
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            string? favoriteIdToSync = null;
            string? favoriteUrlToSync = null;

            UpdateTab(tabId, tab =>
            {
                if (string.Equals(tab.Title, title, StringComparison.Ordinal))
                {
                    return tab;
                }

                if (tab.IsFavorite && !string.IsNullOrWhiteSpace(tab.FavoriteId))
                {
                    favoriteIdToSync = tab.FavoriteId;
                    favoriteUrlToSync = tab.Url;
                }

                return tab with { Title = title };
            });

            if (!string.IsNullOrWhiteSpace(favoriteIdToSync) &&
                !string.IsNullOrWhiteSpace(favoriteUrlToSync))
            {
                try
                {
                    FavoritesService.UpsertFavorite(favoriteIdToSync, favoriteUrlToSync, title);
                    RefreshFavoritesState();
                }
                catch
                {
                }
            }
        };

        _setNavAvailability = SetNavAvailabilityIfNeeded;
        _setAddressFromCore = SetAddressIfNeeded;
        _setLoadingStateFromCore = SetLoadingIfNeeded;
        _refreshHistoryFromCore = () => RefreshHistoryState();

        var selectedIndex = Array.FindIndex(tabs, tab => tab.Id == selectedTag);

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        var titleBar = BrowserChrome.BuildTitleBar(
                         selectedTab,
                         addressText,
                         isTabsCollapsed,
                         canGoBack,
                         canGoForward,
                         () =>
                         {
                             setIsTabsCollapsed(!isTabsCollapsed);
                             RefreshWebViewLayout();
                         },
                         () => _webView?.GoBack(),
                         () => _webView?.CoreWebView2?.Reload(),
                         () => _webView?.GoForward(),
                         setAddressText,
                          SubmitAddress,
                         NavigateActiveTab,
                          selectedSearchProviderKey,
                          BrowserSearchProviders.Providers,
                          SetDefaultSearchProvider,
                         ToggleFavorite,
                         AddTab,
                         CloseActiveTab);

        var tabRail = BrowserChrome.BuildTabRail(
            tabs,
            selectedIndex,
            selectedTag,
            isTabsCollapsed,
            isLoading,
            SelectTab,
            ToggleFavoriteTab,
            CloseTab,
            ReloadTab,
            activeCommandCenterSection == CommandCenterSection.None ? string.Empty : activeCommandCenterSection.ToString(),
            isCommandCenterExpanded,
            mostVisitedHistory,
            recentHistory,
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
            SaveSettingValue,
            ApplyHistoryFilter,
            ApplyFavoritesFilter,
            ImportBrowserHistory,
            ImportBrowserHistoryByName,
            DeleteAllHistory,
            ImportBrowserFavorites,
            ImportBrowserFavoritesByName,
            DeleteAllFavorites,
            OpenHistoryItem,
            ToggleCommandCenterByName,
            ToggleCommandCenterExpanded,
            isRailTabsExpanded,
            MaximizeRailTabsCard,
            MinimizeRailTabsCard,
            DismissCommandCenter);

        var browserContent = BuildBrowserContent(
            selectedTab,
            UpdateTab,
            OpenUriInNewTab,
            isCommandCenterOpen,
            DismissCommandCenter);

        return FlexColumn(
            titleBar,
            FlexRow(
                tabRail,
                browserContent
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
            )
            .Flex(grow: 1, basis: 0)
        );
    }
    private Element BuildBrowserContent(
    BrowserTab selectedTab,
    Action<string, Func<BrowserTab, BrowserTab>> updateTab,
    Action<string> openUriInNewTab,
    bool isCommandCenterOpen,
    Action dismissCommandCenter)
    {
        return Border(null)
        .Set(host =>
        {
            _webViewHost = host;

            host.Tapped -= OnBrowserContentTapped;
            host.Tapped += OnBrowserContentTapped;

            _ = ShowSelectedWebViewAsync(host, selectedTab, updateTab, openUriInNewTab);

            void OnBrowserContentTapped(object? sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
            {
                if (isCommandCenterOpen)
                {
                    dismissCommandCenter();
                }
            }
        })
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch)
        .Flex(grow: 1, basis: 0)
        .MinHeight(300);
    }

    private async Task ShowSelectedWebViewAsync(
    Microsoft.UI.Xaml.Controls.Border host,
    BrowserTab tab,
    Action<string, Func<BrowserTab, BrowserTab>> updateTab,
    Action<string> openUriInNewTab)
    {
        var isNewWebView = false;

        if (!_webViewsByTabId.TryGetValue(tab.Id, out var webView))
        {
            webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 300
            };

            _webViewsByTabId[tab.Id] = webView;
            isNewWebView = true;
        }

        _webView = webView;
        _activeWebViewTabId = tab.Id;

        if (webView.CoreWebView2 is null)
        {
            await webView.EnsureCoreWebView2Async();
        }

        var core = webView.CoreWebView2;

        if (core is not null && _hookedWebViewTabs.Add(tab.Id))
        {
            void SyncTabFromCore(bool completeLoading)
            {
                var currentUrl = core.Source;

                if (string.IsNullOrWhiteSpace(currentUrl))
                {
                    currentUrl = tab.Url;
                }

                var currentTitle = string.IsNullOrWhiteSpace(core.DocumentTitle)
                    ? null
                    : core.DocumentTitle;

                var urlChanged = false;
                string? favoriteIdToSync = null;
                string? favoriteTitleToSync = null;

                updateTab(tab.Id, current =>
                {
                    urlChanged = !BrowserUrl.AreEqual(current.Url, currentUrl);
                    var nextTitle = string.IsNullOrWhiteSpace(currentTitle)
                        ? current.Title
                        : currentTitle;
                    var titleChanged = !string.Equals(current.Title, nextTitle, StringComparison.Ordinal);

                    if (!urlChanged && !titleChanged)
                    {
                        return current;
                    }

                    if (current.IsFavorite && !string.IsNullOrWhiteSpace(current.FavoriteId))
                    {
                        favoriteIdToSync = current.FavoriteId;
                        favoriteTitleToSync = nextTitle;
                    }

                    return current with
                    {
                        Url = currentUrl,
                        Title = nextTitle,
                        DateTime = DateTime.Now,
                        VisitedCount = urlChanged
                            ? current.VisitedCount + 1
                            : current.VisitedCount
                    };
                });

                var historyRecorded = false;

                if (urlChanged)
                {
                    try
                    {
                        TabPersistenceService.UpdateTabVisit(
                            "tabs",
                            tab.Id,
                            incrementVisitCount: true,
                            newUrl: currentUrl,
                            urlChanged: true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        HistoryPersistenceService.RecordVisit(currentUrl, currentTitle);
                        historyRecorded = true;
                    }
                    catch
                    {
                    }
                }
                else if (completeLoading)
                {
                    try
                    {
                        if (!HistoryPersistenceService.ContainsUrl(currentUrl))
                        {
                            HistoryPersistenceService.RecordVisit(currentUrl, currentTitle);
                            historyRecorded = true;
                        }
                    }
                    catch
                    {
                    }
                }

                if (historyRecorded)
                {
                    _refreshHistoryFromCore?.Invoke();
                }

                if (string.Equals(_activeWebViewTabId, tab.Id, StringComparison.Ordinal))
                {
                    if (completeLoading)
                    {
                        _setLoadingStateFromCore?.Invoke(false);
                    }

                    _setAddressFromCore?.Invoke(currentUrl);
                    _setNavAvailability?.Invoke(core.CanGoBack, core.CanGoForward);
                }
            }

            webView.NavigationStarting += (_, _) =>
            {
                if (string.Equals(_activeWebViewTabId, tab.Id, StringComparison.Ordinal))
                {
                    _setLoadingStateFromCore?.Invoke(true);
                    _setNavAvailability?.Invoke(core.CanGoBack, core.CanGoForward);
                }
            };

            webView.NavigationCompleted += async (_, _) =>
            {
                SyncTabFromCore(completeLoading: true);

                await RestoreScrollPositionAsync(tab.Id, tab.ScrollX, tab.ScrollY);
            };

            core.HistoryChanged += (_, _) =>
            {
                SyncTabFromCore(completeLoading: false);
            };  

            core.NewWindowRequested += (_, e) =>
            {
                openUriInNewTab(e.Uri);
                e.Handled = true;
            };

            core.DocumentTitleChanged += (_, _) =>
            {
                var title = core.DocumentTitle;

                if (!string.IsNullOrWhiteSpace(title))
                {
                    _setTitleFromCore?.Invoke(tab.Id, title);
                }
            };

        }

        if (isNewWebView)
        {
            webView.Source = new Uri(tab.Url);
        }
        else if (core is not null)
        {
            _setNavAvailability?.Invoke(core.CanGoBack, core.CanGoForward);
        }

        AttachWebViewToHost(host, webView);
    }

    private static void AttachWebViewToHost(
        Microsoft.UI.Xaml.Controls.Border host,
        Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        host.DispatcherQueue.TryEnqueue(() =>
        {
            if (webView.Parent is Microsoft.UI.Xaml.Controls.Border previousHost &&
                previousHost != host)
            {
                previousHost.Child = null;
            }

            if (host.Child != webView)
            {
                host.Child = webView;
            }

            webView.Visibility = Visibility.Visible;
            webView.InvalidateMeasure();
            webView.InvalidateArrange();
            webView.UpdateLayout();
        });
    }

    void RefreshWebViewLayout()
    {
        if (_webView is null)
        {
            return;
        }

        _webView.DispatcherQueue.TryEnqueue(() =>
        {
            _webView.InvalidateMeasure();
            _webView.InvalidateArrange();
            _webView.UpdateLayout();
        });
    }

    private async Task PauseMediaInTabAsync(string tabId)
    {
        if (!_webViewsByTabId.TryGetValue(tabId, out var webView) ||
            webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync(PauseMediaScript);
        }
        catch
        {
        }
    }

    private async Task CaptureScrollPositionAsync(
    string tabId,
    Action<string, Func<BrowserTab, BrowserTab>> updateTab)
    {
        if (!_webViewsByTabId.TryGetValue(tabId, out var webView) ||
            webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var json = await webView.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({ x: window.scrollX || 0, y: window.scrollY || 0 })");

            var encoded = JsonSerializer.Deserialize<string>(json);

            if (string.IsNullOrWhiteSpace(encoded))
            {
                return;
            }

            using var document = JsonDocument.Parse(encoded);
            var root = document.RootElement;

            var x = root.TryGetProperty("x", out var xNode) ? xNode.GetDouble() : 0;
            var y = root.TryGetProperty("y", out var yNode) ? yNode.GetDouble() : 0;

            updateTab(tabId, tab => tab with
            {
                ScrollX = Math.Max(0, x),
                ScrollY = Math.Max(0, y),
                DateTime = DateTime.Now
            });
        }
        catch
        {
        }
    }

    private async Task RestoreScrollPositionAsync(string tabId, double scrollX, double scrollY)
    {
        if (scrollX <= 0 && scrollY <= 0)
        {
            return;
        }

        if (!_webViewsByTabId.TryGetValue(tabId, out var webView) ||
            webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.scrollTo({scrollX}, {scrollY});");
        }
        catch
        {
        }
    }

    private static BrowserTab[] LoadStartupTabs()
    {
        try
        {
            var persisted = TabPersistenceService.LoadTabs<BrowserTab[]>("tabs");

            if (persisted is not null && persisted.Length > 0)
            {
                var safeTabs = SanitizeTabs(persisted);

                if (safeTabs.Length > 0)
                {
                    var reconciledTabs = ReconcileTabsWithPersistedFavorites(safeTabs);

                    if (!reconciledTabs.SequenceEqual(safeTabs))
                    {
                        TabPersistenceService.SaveTabs("tabs", reconciledTabs);
                    }

                    return reconciledTabs;
                }
            }
        }
        catch
        {
        }

        return [BrowserTab.CreateHome()];
    }

    private static BrowserTab[] ReconcileTabsWithPersistedFavorites(BrowserTab[] tabs)
    {
        FavoriteItem[] persistedFavorites;

        try
        {
            persistedFavorites = FavoritesService.GetFavorites().ToArray();
        }
        catch
        {
            persistedFavorites = [];
        }

        var favoritesById = persistedFavorites
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var nextTabs = new BrowserTab[tabs.Length];

        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];

            if (tab.IsFavorite)
            {
                var favoriteId = string.IsNullOrWhiteSpace(tab.FavoriteId)
                    ? Guid.NewGuid().ToString("N")
                    : tab.FavoriteId;

                if (!favoritesById.ContainsKey(favoriteId))
                {
                    try
                    {
                        favoritesById[favoriteId] = FavoritesService.UpsertFavorite(favoriteId, tab.Url, tab.Title);
                    }
                    catch
                    {
                    }
                }

                nextTabs[index] = tab with
                {
                    FavoriteId = favoriteId,
                    IsFavorite = true
                };
                continue;
            }

            nextTabs[index] = string.IsNullOrWhiteSpace(tab.FavoriteId)
                ? tab
                : tab with
                {
                    FavoriteId = string.Empty,
                    IsFavorite = false
                };
        }

        return nextTabs;
    }

    private static HistoryItem[] LoadRecentHistoryItems(string? filter, int limit = 50)
    {
        try
        {
            return string.IsNullOrWhiteSpace(filter)
                ? HistoryPersistenceService.GetRecentHistory(limit).ToArray()
                : HistoryPersistenceService.SearchHistory(filter, limit).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string[] GetHistoryImportBrowserNames()
    {
        try
        {
            return BrowserHistoryImportService.DiscoverSources()
                .Select(source => source.BrowserName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string[] GetFavoritesImportBrowserNames()
    {
        try
        {
            return BrowserFavoritesImportService.DiscoverSources()
                .Select(source => source.BrowserName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static HistoryItem[] LoadMostVisitedHistoryItems(int limit = 12)
    {
        try
        {
            return HistoryPersistenceService.GetMostVisited(limit).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static FavoriteItem[] LoadFavoriteItems(string? filter)
    {
        try
        {
            return string.IsNullOrWhiteSpace(filter)
                ? FavoritesService.GetFavorites().ToArray()
                : FavoritesService.SearchFavorites(filter).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private void RegisterShutdownSave()
    {
        if (_shutdownSaveRegistered)
        {
            return;
        }

        _shutdownSaveRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushTabsSave();
    }

    private void FlushTabsSave()
    {
        var selectedTabId = _latestSelectedTabId;
        var tabs = _latestTabs;

        if (tabs.Length == 0 || string.IsNullOrWhiteSpace(selectedTabId))
        {
            return;
        }

        try
        {
            _saveTabsCts?.Cancel();
            _saveTabsCts?.Dispose();
            _saveTabsCts = null;

            TabPersistenceService.SaveTabs("tabs", tabs);
            TabPersistenceService.SaveTabs("selectedTabId", selectedTabId);
        }
        catch
        {
        }
    }

    private void ScheduleTabsSave(BrowserTab[] tabs, string selectedTabId)
    {
        _latestTabs = tabs;
        _latestSelectedTabId = selectedTabId;

        _saveTabsCts?.Cancel();
        _saveTabsCts?.Dispose();

        var snapshotTabs = tabs.ToArray();
        var snapshotSelectedTabId = selectedTabId;
        var cts = new CancellationTokenSource();

        _saveTabsCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, cts.Token);

                TabPersistenceService.SaveTabs("tabs", snapshotTabs);
                TabPersistenceService.SaveTabs("selectedTabId", snapshotSelectedTabId);
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }
    static BrowserTab[] SanitizeTabs(BrowserTab[] tabs)
    {
        return tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Id))
            .Where(tab => Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .GroupBy(tab => tab.Id)
            .Select(group => group.First())
            .OrderBy(tab => tab.Order)
            .Take(MaxTabs)
            .Select((tab, index) => tab with
            {
                Title = Trim(tab.Title, MaxTitleLength),
                Url = Trim(tab.Url, MaxUrlLength),
                VisitedCount = Math.Max(0, tab.VisitedCount),
                Order = index,
                ScrollX = Math.Max(0, tab.ScrollX),
                ScrollY = Math.Max(0, tab.ScrollY)
            })
            .ToArray();

        static string Trim(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }
}