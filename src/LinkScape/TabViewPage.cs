using LinkScape.Browser;
using LinkScape.Browser.State;
using LinkScape.Models;
using Browser.Components;
using Microsoft.UI.Dispatching;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape;

class TabViewPage : Component
{
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";
    private const string HomeUrlSettingKey = BrowserConstants.HomeUrlSettingKey;
    private const double BrowserSurfaceInsetCollapsed = 8;
    private const double BrowserSurfaceInsetExpanded = 10;

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

    private CancellationTokenSource? _saveTabsCts;
    private string? _latestSelectedTabId;
    private bool _shutdownSaveRegistered;
    private const int MaxTabs = 50;
    private const int MaxTitleLength = 256;
    private const int MaxUrlLength = 2048;
    private readonly BrowserTitleBarController _browserTitleBarController = new();
    private readonly BrowserWebViewHostController _browserWebViewHostController = new();
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _importBrowserNamesLoadStarted;
    private bool _activationListenerRegistered;
    private int _commandCenterBusyVersion;
    private int _commandCenterHighlightVersion;
    private BrowserSessionState _latestBrowserSession;
    private BrowserTab[] _latestTabs = [];
    private readonly BrowserTab[] _startupTabs;
    private readonly string _startupSelectedTabId;
    private Action<string>? _openActivatedTarget;
    private bool _browserNoticeListenerRegistered;

    public TabViewPage()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var startupTabs = LoadStartupTabs();
        var selectedSearchProviderDefault = BrowserSearchProviders.NormalizeProviderKey(
            SettingsService.GetValueOrDefault(
                DefaultSearchProviderSettingKey,
                BrowserSearchProviders.DefaultProviderKey));

        string startupSelectedTabId;

        if (ActivationRoutingService.TryConsumePendingTarget(out var activationTarget))
        {
            startupTabs = AddActivatedStartupTab(startupTabs, activationTarget, selectedSearchProviderDefault, out var activatedTab);
            startupSelectedTabId = activatedTab.Id;
        }
        else
        {
            var persistedSelectedTabId = TabPersistenceService.LoadTabs<string>("selectedTabId");
            var selectedTab = startupTabs.FirstOrDefault(tab => tab.Id == persistedSelectedTabId) ?? startupTabs[0];
            startupSelectedTabId = selectedTab.Id;
        }

        _startupTabs = startupTabs;
        _startupSelectedTabId = startupSelectedTabId;
        _latestTabs = _startupTabs;
        _latestSelectedTabId = _startupSelectedTabId;
        _latestBrowserSession = BrowserSessionState.Create(
            _startupTabs,
            _startupSelectedTabId,
            selectedSearchProviderDefault);

        RegisterShutdownSave();
    }

    public override Element Render()
    {
        var selectedSearchProviderDefault = BrowserSearchProviders.NormalizeProviderKey(
            SettingsService.GetValueOrDefault(
                DefaultSearchProviderSettingKey,
                BrowserSearchProviders.DefaultProviderKey));

        var (session, setSession) = UseState(_latestBrowserSession);

        _latestBrowserSession = session;
        var tabs = session.Tabs;
        _latestTabs = tabs;
        var selectedTag = session.SelectedTabId;
        _latestSelectedTabId = selectedTag;
        var isTabsCollapsed = session.IsTabsCollapsed;
        var canGoBack = session.CanGoBack;
        var canGoForward = session.CanGoForward;
        var isLoading = session.IsLoading;
        var (historyFilter, setHistoryFilter) = UseState(string.Empty);
        var (recentHistory, setRecentHistory) = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var (mostVisitedHistory, setMostVisitedHistory) = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var (favoritesFilter, setFavoritesFilter) = UseState(string.Empty);
        var (favoriteItems, setFavoriteItems) = UseState(Array.Empty<FavoriteItem>(), threadSafe: true);
        var (favoritesImportStatus, setFavoritesImportStatus) = UseState(string.Empty, threadSafe: true);
        var (historyImportStatus, setHistoryImportStatus) = UseState(string.Empty, threadSafe: true);
        var (isCommandCenterBusy, setIsCommandCenterBusy) = UseState(false, threadSafe: true);
        var (isCommandCenterHighlighted, setIsCommandCenterHighlighted) = UseState(false, threadSafe: true);
        var (commandCenterBusyText, setCommandCenterBusyText) = UseState(string.Empty, threadSafe: true);
        var (historyImportBrowserNames, setHistoryImportBrowserNames) = UseState(Array.Empty<string>(), threadSafe: true);
        var (favoritesImportBrowserNames, setFavoritesImportBrowserNames) = UseState(Array.Empty<string>(), threadSafe: true);
        var activeCommandCenterSection = session.ActiveCommandCenterSection;
        var isCommandCenterExpanded = session.IsCommandCenterExpanded;
        var isRailTabsExpanded = session.IsRailTabsExpanded;
        var (settingsSnapshot, setSettingsSnapshot) = UseState<IReadOnlyDictionary<string, string>>(SettingsService.Dump());
        var (browserNotice, setBrowserNotice) = UseState<BrowserNotice?>(BrowserNoticeService.CurrentNotice, threadSafe: true);
        var selectedSearchProviderKey = session.SelectedSearchProviderKey;
        var isCommandCenterOpen = session.IsCommandCenterOpen;
        var configuredHomeUrl = GetConfiguredHomeUrl(settingsSnapshot);

        RegisterBrowserNoticeListener(setBrowserNotice);

        if (!_importBrowserNamesLoadStarted)
        {
            _importBrowserNamesLoadStarted = true;
            _ = Task.Run(() =>
            {
                setHistoryImportBrowserNames(GetHistoryImportBrowserNames());
                setFavoritesImportBrowserNames(GetFavoritesImportBrowserNames());
            });
        }

        #region Event Handlers

        void UpdateBrowserSession(Func<BrowserSessionState, BrowserSessionState> updater)
        {
            _latestBrowserSession = updater(_latestBrowserSession);
            _latestTabs = _latestBrowserSession.Tabs;
            _latestSelectedTabId = _latestBrowserSession.SelectedTabId;
            setSession(_latestBrowserSession);
        }
        
        void MarkTabsChanged(BrowserTab[] nextTabs)
        {
            _latestTabs = nextTabs;
            UpdateBrowserSession(state => BrowserSessionStore.SetTabs(state, nextTabs));
            ScheduleTabsSave(nextTabs, _latestSelectedTabId ?? selectedTag);
        }

        void ToggleCommandCenter(CommandCenterSection section)
        {
            var sectionName = section == CommandCenterSection.None ? string.Empty : section.ToString();

            if (string.Equals(activeCommandCenterSection, sectionName, StringComparison.Ordinal))
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetCommandCenterExpanded(state, false));
            }

            var nextSection = string.Equals(activeCommandCenterSection, sectionName, StringComparison.Ordinal)
                ? string.Empty
                : sectionName;

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nextSection));

            switch (nextSection)
            {
                case nameof(CommandCenterSection.History):
                case nameof(CommandCenterSection.Recent):
                case nameof(CommandCenterSection.MostVisited):
                    RefreshHistoryState();
                    break;
                case nameof(CommandCenterSection.Favorites):
                    RefreshFavoritesState();
                    break;
            }
        }

        void ShowCommandCenterSettingsFromTitleBar()
        {
            UpdateBrowserSession(state =>
            {
                var nextState = BrowserSessionStore.SetTabsCollapsed(state, false);
                nextState = BrowserSessionStore.SetRailTabsExpanded(nextState, true);
                nextState = BrowserSessionStore.SetActiveCommandCenterSection(nextState, nameof(CommandCenterSection.Settings));
                return BrowserSessionStore.SetCommandCenterExpanded(nextState, false);
            });

            var version = Interlocked.Increment(ref _commandCenterHighlightVersion);
            setIsCommandCenterHighlighted(true);

            _ = Task.Run(async () =>
            {
                await Task.Delay(1800);

                if (version == Volatile.Read(ref _commandCenterHighlightVersion))
                {
                    setIsCommandCenterHighlighted(false);
                }
            });
        }

        void DismissCommandCenter()
        {
            UpdateBrowserSession(BrowserSessionStore.DismissCommandCenter);
        }

        void ToggleCommandCenterExpanded()
        {
            if (!isCommandCenterOpen)
            {
                return;
            }

            var nextExpanded = !isCommandCenterExpanded;
            var nextSession = BrowserSessionStore.SetCommandCenterExpanded(session, nextExpanded);

            if (nextExpanded)
            {
                nextSession = BrowserSessionStore.SetRailTabsExpanded(nextSession, false);
            }

            UpdateBrowserSession(_ => nextSession);
        }

        void MaximizeRailTabsCard()
        {
            UpdateBrowserSession(BrowserSessionStore.MaximizeRailTabs);
        }

        void MinimizeRailTabsCard()
        {
            UpdateBrowserSession(BrowserSessionStore.MinimizeRailTabs);
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
            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedSearchProvider(state, normalizedProviderKey));
            setSettingsSnapshot(SettingsService.Dump());
        }

        void SaveSettingValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (string.Equals(key, HomeUrlSettingKey, StringComparison.Ordinal))
            {
                value = NormalizeHomeUrl(value);
            }

            SettingsService.SetValue(key, value);
            setSettingsSnapshot(SettingsService.Dump());

            if (string.Equals(key, DefaultSearchProviderSettingKey, StringComparison.Ordinal))
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetSelectedSearchProvider(
                    state,
                    BrowserSearchProviders.NormalizeProviderKey(value)));
            }
        }

        void SetCurrentPageAsHome()
        {
            var currentUrl = tabs.FirstOrDefault(tab => tab.Id == selectedTag)?.Url;

            if (string.IsNullOrWhiteSpace(currentUrl))
            {
                return;
            }

            SaveSettingValue(HomeUrlSettingKey, currentUrl);
        }

        void OpenUriInNewTab(string rawUrl, bool dismissCommandCenter = true)
        {
            if (dismissCommandCenter)
            {
                DismissCommandCenter();
            }

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;

            if (currentTabs.Length >= MaxTabs)
            {
                return;
            }

            var target = BrowserUrl.Normalize(rawUrl, configuredHomeUrl, selectedSearchProviderKey);
            var nextTabs = BrowserTabActions.Add(
                currentTabs,
                target,
                out var newTab,
                visitCount: 1);

            MarkTabsChanged(nextTabs);

            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, newTab.Id));
            _browserTitleBarController.SetAddressText(newTab.Url);
            ScheduleTabsSave(nextTabs, newTab.Id);
        }

        _openActivatedTarget = target => OpenUriInNewTab(target, dismissCommandCenter: false);
        RegisterActivationListener();

        void UpdateTab(string id, Func<BrowserTab, BrowserTab> updater)
        {
            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
            var nextTabs = BrowserTabActions.Replace(currentTabs, id, updater, out var changed);

            if (changed)
            {
                MarkTabsChanged(nextTabs);
            }
        }

        void SetNavAvailabilityIfNeeded(bool back, bool forward)
        {
            if (_latestBrowserSession.CanGoBack != back)
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetNavAvailability(state, back, forward));
                return;
            }

            if (_latestBrowserSession.CanGoForward != forward)
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetNavAvailability(state, back, forward));
            }
        }

        void SetLoadingIfNeeded(bool next)
        {
            if (_latestBrowserSession.IsLoading != next)
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetLoading(state, next));
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

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.History)));
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

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.History)));
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

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.Favorites)));
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

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.Favorites)));
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

            NavigateActiveTab(url);
        }

        void OpenHistoryItemInNewTab(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            OpenUriInNewTab(url, dismissCommandCenter: false);
        }

        void OpenFavoriteItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            NavigateActiveTab(url);
        }

        void OpenFavoriteItemInNewTab(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            OpenUriInNewTab(url, dismissCommandCenter: false);
        }

        void NavigateActiveTab(string rawUrl)
        {
            var activeId = selectedTag;
            var fallback = tabs.FirstOrDefault(tab => tab.Id == activeId)?.Url ?? configuredHomeUrl;
            var target = BrowserUrl.Normalize(rawUrl, fallback, selectedSearchProviderKey);

            _browserTitleBarController.SetAddressText(target);

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

            _browserWebViewHostController.Navigate(activeId, target);
        }

        void SubmitAddress(string rawUrl)
        {
            var currentUrl = tabs.FirstOrDefault(tab => tab.Id == selectedTag)?.Url;
            var fallback = currentUrl ?? configuredHomeUrl;
            var target = BrowserUrl.Normalize(rawUrl, fallback, selectedSearchProviderKey);

            NavigateActiveTab(target);
        }

        void AddTab()
        {
            UpdateBrowserSession(BrowserSessionStore.MaximizeRailTabs);

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;

            if (currentTabs.Length >= MaxTabs)
            {
                return;
            }

            var nextTabs = BrowserTabActions.Add(
                currentTabs,
                BrowserSearchProviders.GetHomeUrl(selectedSearchProviderKey),
                out var newTab);

            MarkTabsChanged(nextTabs);

            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, newTab.Id));
            _browserTitleBarController.SetAddressText(newTab.Url);

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

            var index = Array.FindIndex(currentTabs, tab => tab.Id == tabId);

            if (index < 0)
            {
                return;
            }

            var wasSelected = string.Equals(selectedTag, tabId, StringComparison.Ordinal);
            var nextTabs = BrowserTabActions.Close(currentTabs, tabId, configuredHomeUrl, out var nextTab);

            if (nextTab is null)
            {
                return;
            }

            _browserWebViewHostController.CloseTab(tabId);
            MarkTabsChanged(nextTabs);

            if (wasSelected)
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, nextTab.Id));
                _browserTitleBarController.SetAddressText(nextTab.Url);
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
            _browserWebViewHostController.ReloadTab(tabId);
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
                _ = _browserWebViewHostController.CaptureScrollPositionAsync(previousTabId);
                _ = _browserWebViewHostController.PauseMediaInTabAsync(previousTabId);

                UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, nextTab.Id));
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

            _browserTitleBarController.SetAddressText(nextTab.Url);
        }
        #endregion

        var selectedTab = tabs.FirstOrDefault(tab => tab.Id == selectedTag) ?? tabs[0];

        void SetTitleFromCore(string tabId, string title)
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
        }

        var selectedIndex = Array.FindIndex(tabs, tab => tab.Id == selectedTag);

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        var titleBar = Component<BrowserTitleBar, BrowserTitleBarProps>(
            new BrowserTitleBarProps(
                _browserTitleBarController,
                selectedTab,
                configuredHomeUrl,
                isTabsCollapsed,
                canGoBack,
                canGoForward,
                () =>
                {
                    UpdateBrowserSession(state => BrowserSessionStore.SetTabsCollapsed(state, !isTabsCollapsed));
                },
                () => _browserWebViewHostController.GoBack(),
                () => _browserWebViewHostController.Reload(),
                () => _browserWebViewHostController.GoForward(),
                SubmitAddress,
                NavigateActiveTab,
                selectedSearchProviderKey,
                BrowserSearchProviders.Providers,
                SetDefaultSearchProvider,
                SetCurrentPageAsHome,
                ToggleFavorite,
                ShowCommandCenterSettingsFromTitleBar,
                AddTab,
                CloseActiveTab));

        var tabRail = Component<BrowserTabRail, BrowserTabRailProps>(
            new BrowserTabRailProps(
                tabs,
                selectedIndex,
                selectedTag,
                isTabsCollapsed,
                isLoading,
                SelectTab,
                ToggleFavoriteTab,
                CloseTab,
                ReloadTab,
                activeCommandCenterSection,
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
                isCommandCenterHighlighted,
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
                OpenHistoryItemInNewTab,
                OpenFavoriteItem,
                OpenFavoriteItemInNewTab,
                ToggleCommandCenterByName,
                ToggleCommandCenterExpanded,
                isRailTabsExpanded,
                MaximizeRailTabsCard,
                MinimizeRailTabsCard,
                DismissCommandCenter,
                () => _browserWebViewHostController.RefreshLayout()));

        var browserContent = Component<BrowserWebViewHost, BrowserWebViewHostProps>(
            new BrowserWebViewHostProps(
                _browserWebViewHostController,
                selectedTab,
                isCommandCenterOpen,
                () =>
                {
                    _browserTitleBarController.SetAddressText(selectedTab.Url);

                    if (isCommandCenterOpen)
                    {
                        DismissCommandCenter();
                    }
                },
                UpdateTab,
                url => OpenUriInNewTab(url),
                SetTitleFromCore,
                SetNavAvailabilityIfNeeded,
                nextAddress => _browserTitleBarController.SetAddressText(nextAddress, preserveUserEdit: true),
                SetLoadingIfNeeded,
                () => RefreshHistoryState()));

        var browserSurface = Border(
            (FlexRow(
                Border(
                    Border(null)
                        .Width(1)
                        .Background(Theme.SurfaceStroke)
                        .Opacity(isTabsCollapsed ? 0.16 : 0.36)
                        .HAlign(HorizontalAlignment.Right)
                        .VAlign(VerticalAlignment.Stretch)
                )
                .Width(isTabsCollapsed ? BrowserSurfaceInsetCollapsed : BrowserSurfaceInsetExpanded)
                .VAlign(VerticalAlignment.Stretch).CornerRadius(14)
                .Flex(shrink: 0),
                browserContent
                    .HAlign(HorizontalAlignment.Stretch)
                    .Flex(grow: 1, basis: 0)
            ) with
            {
                ColumnGap = 0
            })
        )
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch)
        .MinWidth(0)
        .CornerRadius(12)
        .Flex(grow: 1, basis: 0);

        return FlexColumn(
            titleBar,
            BuildBrowserNoticeBanner(browserNotice),
            FlexRow(
                tabRail,
                browserSurface
            ).Backdrop(BackdropKind.Transparent)
           .Flex(grow: 1, basis: 0)
        );
    }

    private void RegisterBrowserNoticeListener(Action<BrowserNotice?> setBrowserNotice)
    {
        if (_browserNoticeListenerRegistered)
        {
            return;
        }

        _browserNoticeListenerRegistered = true;
        BrowserNoticeService.NoticeChanged += OnBrowserNoticeChanged;

        void OnBrowserNoticeChanged()
        {
            setBrowserNotice(BrowserNoticeService.CurrentNotice);
        }
    }

    private static Element BuildBrowserNoticeBanner(BrowserNotice? browserNotice)
    {
        if (browserNotice is null || string.IsNullOrWhiteSpace(browserNotice.Message))
        {
            return Border(null).Height(0).Flex(shrink: 0);
        }

        return Border(
            (FlexRow(
                BrowserIcons.FluentIcon("⚠", 14),
                (TextBlock(browserNotice.Message) with
                {
                    TextWrapping = TextWrapping.WrapWholeWords
                })
                .Flex(grow: 1, basis: 0),
                Button("Dismiss", BrowserNoticeService.Clear)
                    .AutomationName("Dismiss browser notice")
                    .Height(30)
                    .Padding(10, 0)
                    .CornerRadius(15)
            ) with
            {
                ColumnGap = 10
            })
            .VAlign(VerticalAlignment.Center)
        )
        .Padding(12, 10)
        .Margin(8, 4, 8, 0)
        .CornerRadius(16)
        .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(shrink: 0);
    }

    #region Data_Management

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

        return [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
    }

    private static BrowserTab[] AddActivatedStartupTab(
        BrowserTab[] tabs,
        string activationTarget,
        string selectedSearchProviderKey,
        out BrowserTab activatedTab)
    {
        var fallback = GetConfiguredHomeUrl();
        var currentTabs = tabs.Length > 0 ? tabs : [BrowserTab.CreateHome(fallback)];

        if (currentTabs.Length >= MaxTabs)
        {
            activatedTab = currentTabs[^1];
            return currentTabs;
        }

        var normalizedTarget = BrowserUrl.Normalize(activationTarget, fallback, selectedSearchProviderKey);
        return BrowserTabActions.Add(currentTabs, normalizedTarget, out activatedTab, visitCount: 1);
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

    private void RegisterActivationListener()
    {
        if (_activationListenerRegistered)
        {
            return;
        }

        _activationListenerRegistered = true;
        ActivationRoutingService.ActivationRequested += OnActivationRequested;
    }

    private void OnActivationRequested()
    {
        void OpenPendingTarget()
        {
            if (ActivationRoutingService.TryConsumePendingTarget(out var target))
            {
                _openActivatedTarget?.Invoke(target);
            }
        }

        if (_dispatcherQueue?.HasThreadAccess ?? true)
        {
            OpenPendingTarget();
            return;
        }

        _dispatcherQueue?.TryEnqueue(OpenPendingTarget);
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
                && (uri.Scheme == Uri.UriSchemeHttp
                    || uri.Scheme == Uri.UriSchemeHttps
                    || (uri.IsFile && uri.LocalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))))
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

    private static string GetConfiguredHomeUrl(IReadOnlyDictionary<string, string>? settingsSnapshot = null)
    {
        var configuredHomeUrl = settingsSnapshot is not null &&
            settingsSnapshot.TryGetValue(HomeUrlSettingKey, out var snapshotHomeUrl)
                ? snapshotHomeUrl
                : SettingsService.GetValueOrDefault(HomeUrlSettingKey, BrowserConstants.HomeUrl);

        return NormalizeHomeUrl(configuredHomeUrl);
    }

    private static string NormalizeHomeUrl(string? value)
    {
        return BrowserUrl.Normalize(value ?? string.Empty, BrowserConstants.HomeUrl);
    }

    #endregion
}