using LinkScape.Browser;
using LinkScape.Browser.Messages;
using LinkScape.Browser.State;
using LinkScape.Models;
using LinkScape.Browser.Components;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape.Browser;

class TabViewPage : Component
{
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";
    private const string HomeUrlSettingKey = BrowserConstants.HomeUrlSettingKey;
    private const string SaveTabsSettingKey = BrowserConstants.SaveTabsSettingKey;
    private const double BrowserSurfaceInsetCollapsed = 2;
    private const double BrowserSurfaceInsetExpanded = 4;
    private const int CommandCenterBusyMinimumDurationMilliseconds = 220;
    private const int InitialFavoriteQueryLimit = 150;
    private const int FilterDebounceMilliseconds = 175;

    private enum CommandCenterSection
    {
        None,
        History,
        Recent,
        MostVisited,
        Backdrop,
        Favorites,
        Collections,
        Chat
    }

    private sealed record FirstRunImportNotice(string Message, bool IsBusy, bool HasErrors);

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
    private bool _collectionStateLoadStarted;
    private bool _activationListenerRegistered;
    private int _commandCenterBusyVersion;
    private int _commandCenterHighlightVersion;
    private DateTime _commandCenterBusyStartedAtUtc;
    private BrowserSessionState _latestBrowserSession;
    private BrowserTab[] _latestTabs = [];
    private readonly List<string> _tabActivationHistory = [];
    private string? _lastTrackedSelectedTabId;
    private readonly BrowserTab[] _startupTabs;
    private readonly string _startupSelectedTabId;
    private Action<ActivationTarget>? _openActivatedTarget;
    private ActivationTarget? _deferredStartupActivation;
    private bool _deferredStartupActivationQueued;
    private string? _deferredWhatsNewVersion;
    private bool _deferredWhatsNewQueued;
    private bool _suppressTabPersistence;
    private CancellationTokenSource? _historyFilterCts;
    private CancellationTokenSource? _favoritesFilterCts;
    private bool _browserNoticeListenerRegistered;
    private bool _fullScreenPresentationMessengerRegistered;
    private Action<bool>? _setFullScreenPresentationState;
    private static IMessenger Messenger => LinkScapeServiceProvider.GetRequiredService<IMessenger>();

    public TabViewPage()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var startupTabs = LoadStartupTabs();
        var selectedSearchProviderDefault = BrowserSearchProviders.NormalizeProviderKey(
            SettingsService.GetValueOrDefault(
                DefaultSearchProviderSettingKey,
                BrowserSearchProviders.DefaultProviderKey));

        string startupSelectedTabId;

        var isOrdinaryBrowserLaunch = true;

        if (ActivationRoutingService.TryConsumePendingTarget(out var activationTarget, out var isFreshWindow))
        {
            isOrdinaryBrowserLaunch = activationTarget.Kind == ActivationTargetKind.MainBrowser;

            if (activationTarget.Kind == ActivationTargetKind.Url)
            {
                if (isFreshWindow)
                {
                    SuppressTabPersistence();
                }
                startupTabs = isFreshWindow
                    ? CreateFreshWindowTabs(activationTarget.Value, selectedSearchProviderDefault, out var activatedTab)
                    : AddActivatedStartupTab(startupTabs, activationTarget.Value, selectedSearchProviderDefault, out activatedTab);
                startupSelectedTabId = activatedTab.Id;
            }
            else if (activationTarget.Kind == ActivationTargetKind.Search)
            {
                SuppressTabPersistence();
                var defaultTab = BrowserTab.CreateHome(
                    BrowserSearchProviders.GetHomeUrl(selectedSearchProviderDefault));
                startupTabs = [defaultTab];
                startupSelectedTabId = defaultTab.Id;
            }
            else if (activationTarget.Kind == ActivationTargetKind.SavedTabs)
            {
                startupTabs = LoadSavedTabs();
                var selectedTab = ResolveStartupSelectedTab(startupTabs);
                startupSelectedTabId = selectedTab.Id;
            }
            else if (activationTarget.Kind == ActivationTargetKind.Collection)
            {
                startupTabs = SetAndLoadStartupCollection(activationTarget.Value);
                var selectedTab = ResolveStartupSelectedTab(startupTabs);
                startupSelectedTabId = selectedTab.Id;
            }
            else if (activationTarget.Kind == ActivationTargetKind.ActiveTabsPackage &&
                ActiveTabsPackage.TryParse(activationTarget.Value, out var package, out _))
            {
                if (!package.ShouldSaveState)
                {
                    SuppressTabPersistence();
                }

                startupTabs = CreateTabsFromPackage(package, selectedSearchProviderDefault, out startupSelectedTabId);
                SavePackageCollection(package);
            }
            else
            {
                var selectedTab = ResolveStartupSelectedTab(startupTabs);
                startupSelectedTabId = selectedTab.Id;
                _deferredStartupActivation = activationTarget;
            }
        }
        else
        {
            var selectedTab = ResolveStartupSelectedTab(startupTabs);
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

        if (isOrdinaryBrowserLaunch && AppUpdateService.TryGetUnseenPackageVersion(out var unseenVersion))
        {
            _deferredWhatsNewVersion = unseenVersion;
        }

        RegisterShutdownSave();
    }

    public override Element Render()
    {
        var selectedSearchProviderDefault = BrowserSearchProviders.NormalizeProviderKey(
            SettingsService.GetValueOrDefault(
                DefaultSearchProviderSettingKey,
                BrowserSearchProviders.DefaultProviderKey));

        var session = UseState(_latestBrowserSession);

        _latestBrowserSession = session.Value;
        var tabs = session.Value.Tabs;
        _latestTabs = tabs;
        var selectedTag = session.Value.SelectedTabId;
        _latestSelectedTabId = selectedTag;
        TrackSelectedTab(selectedTag, tabs);
        var isTabsCollapsed = session.Value.IsTabsCollapsed;
        var canGoBack = session.Value.CanGoBack;
        var canGoForward = session.Value.CanGoForward;
        var isLoading = session.Value.IsLoading;
        var installableWebApps = UseState<IReadOnlyDictionary<string, InstallableWebApp>>(
        new Dictionary<string, InstallableWebApp>(),
        threadSafe: true);
        var historyFilter = UseState(string.Empty);
        var historyLimit = UseState(50);
        var recentHistory = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var mostVisitedHistory = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var favoritesFilter = UseState(string.Empty);
        var favoritesLimit = UseState(InitialFavoriteQueryLimit);
        var favoriteItems = UseState(Array.Empty<FavoriteItem>(), threadSafe: true);
        var tabCollections = UseState(Array.Empty<TabCollection>(), threadSafe: true);
        var collectionItems = UseState(Array.Empty<TabCollectionItem>(), threadSafe: true);
        var collectionMembership = UseState<IReadOnlyDictionary<string, string[]>>(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            threadSafe: true);
        var collectionName = UseState("Personal", threadSafe: true);
        var collectionStatus = UseState(string.Empty, threadSafe: true);
        var favoritesImportStatus = UseState(string.Empty, threadSafe: true);
        var historyImportStatus = UseState(string.Empty, threadSafe: true);
        var isCommandCenterBusy = UseState(false, threadSafe: true);
        var isCommandCenterHighlighted = UseState(false, threadSafe: true);
        var commandCenterBusyText = UseState(string.Empty, threadSafe: true);
        var isLinkerCompactState = UseState(false);
        var historyImportBrowserProfiles = UseState<IReadOnlyDictionary<string, BrowserImportProfile[]>>(
            new Dictionary<string, BrowserImportProfile[]>(StringComparer.OrdinalIgnoreCase),
            threadSafe: true);
        var favoritesImportBrowserProfiles = UseState<IReadOnlyDictionary<string, BrowserImportProfile[]>>(
            new Dictionary<string, BrowserImportProfile[]>(StringComparer.OrdinalIgnoreCase),
            threadSafe: true);
        var activeCommandCenterSection = session.Value.ActiveCommandCenterSection;
        var isCommandCenterExpanded = session.Value.IsCommandCenterExpanded;
        var isRailTabsExpanded = session.Value.IsRailTabsExpanded;
        var settingsSnapshot = UseState<IReadOnlyDictionary<string, string>>(SettingsService.Dump());
        var isFirstRunSetupVisible = UseState(FirstRunExperienceService.ShouldShow());
        var firstRunImportNotice = UseState<FirstRunImportNotice?>(null, threadSafe: true);
        var isFirstRunImportNoticeVisible = UseState(false, threadSafe: true);
        var browserNotice = UseState<BrowserNotice?>(BrowserNoticeService.CurrentNotice, threadSafe: true);
        var isFullScreenPresentationActive = UseState(
            global::LinkScape.Application.MainWindowActivation.IsFullScreenPresentationActive,
            threadSafe: true);
        var selectedSearchProviderKey = session.Value.SelectedSearchProviderKey;
        var isCommandCenterOpen = session.Value.IsCommandCenterOpen;
        var isChatBladeOpen = session.Value.IsChatOpen;
        var configuredHomeUrl = GetConfiguredHomeUrl(settingsSnapshot.Value);


        // value of selectedInstallableWebApp will be null if the selectedTag does not match any installable web app
        installableWebApps.Value.TryGetValue(
            selectedTag,
            out var selectedInstallableWebApp);

        var selectedInstalledWebApp =
    WebAppStateService.FindInstalled(
        selectedInstallableWebApp);

        var isSelectedWebAppInstalled =
            selectedInstalledWebApp is not null;

        RegisterBrowserNoticeListener(browserNotice.Set);
        RegisterFullScreenPresentationMessenger(isFullScreenPresentationActive.Set);

        if (!_importBrowserNamesLoadStarted)
        {
            _importBrowserNamesLoadStarted = true;
            _ = Task.Run(() =>
            {
                historyImportBrowserProfiles.Set(GetHistoryImportBrowserProfiles());
                favoritesImportBrowserProfiles.Set(GetFavoritesImportBrowserProfiles());
            });
        }



        #region Event Handlers

        void UpdateBrowserSession(Func<BrowserSessionState, BrowserSessionState> updater)
        {
            _latestBrowserSession = updater(_latestBrowserSession);
            _latestTabs = _latestBrowserSession.Tabs;
            _latestSelectedTabId = _latestBrowserSession.SelectedTabId;
            session.Set(_latestBrowserSession);
        }

        void EnqueueUiTransition(Action transition)
        {
            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(() => transition());
                return;
            }

            transition();
        }
        void OpenCurrentWebApp()
        {
            if (!WebAppStateService.TryOpenInstalled(
                    selectedInstallableWebApp))
            {
                BrowserNoticeService.Show(
                    "This web app is not installed.");
            }
        }

        string? GetTabInstalledWebAppName(BrowserTab tab)
        {
            return FindInstalledWebAppForTab(tab)?.Name;
        }

        string? GetTabInstallableWebAppName(BrowserTab tab)
        {
            return installableWebApps.Value.TryGetValue(tab.Id, out var app)
                ? app.Name
                : null;
        }

        void OpenTabAsWebApp(string tabId)
        {
            var tab = tabs.FirstOrDefault(candidate => string.Equals(candidate.Id, tabId, StringComparison.Ordinal));
            if (tab is null)
            {
                return;
            }

            var installed = FindInstalledWebAppForTab(tab);
            if (installed is null)
            {
                BrowserNoticeService.Show("This tab does not match an installed app.");
                return;
            }

            WebAppWindowService.Open(installed);
        }

        void InstallTabWebApp(string tabId)
        {
            if (!installableWebApps.Value.TryGetValue(tabId, out var app))
            {
                BrowserNoticeService.Show("This tab is not installable as an app.");
                return;
            }

            try
            {
                if (InstalledWebAppService.IsInstalled(app.ManifestUrl))
                {
                    BrowserNoticeService.Show($"{app.Name} is already installed.", "info");
                    return;
                }

                var installed = InstalledWebAppService.Install(app);
                _ = AppJumpListService.RefreshAsync();
                BrowserNoticeService.Show($"{installed.Name} was installed.", "success");

                var next = new Dictionary<string, InstallableWebApp>(installableWebApps.Value);
                next.Remove(tabId);
                installableWebApps.Set(next);
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not install this app: {ex.Message}");
            }
        }

        InstalledWebApp? FindInstalledWebAppForTab(BrowserTab tab)
        {
            if (installableWebApps.Value.TryGetValue(tab.Id, out var installableApp))
            {
                var installed = WebAppStateService.FindInstalled(installableApp);
                if (installed is not null)
                {
                    return installed;
                }
            }

            return InstalledWebAppService
                .GetAll()
                .FirstOrDefault(app => IsUrlWithinAppScope(tab.Url, app));
        }

        static bool IsUrlWithinAppScope(string? rawUrl, InstalledWebApp app)
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var tabUri) ||
                !Uri.TryCreate(app.Scope, UriKind.Absolute, out var scopeUri))
            {
                return false;
            }

            return string.Equals(tabUri.Scheme, scopeUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tabUri.Host, scopeUri.Host, StringComparison.OrdinalIgnoreCase) &&
                tabUri.Port == scopeUri.Port &&
                tabUri.AbsolutePath.StartsWith(scopeUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }

        void InstallCurrentWebApp()
        {
            if (selectedInstallableWebApp is null)
            {
                return;
            }

            try
            {
                if (InstalledWebAppService.IsInstalled(
                        selectedInstallableWebApp.ManifestUrl))
                {
                    BrowserNoticeService.Show(
                        $"{selectedInstallableWebApp.Name} is already installed.",
                        "info");

                    return;
                }

                var installed =
                    InstalledWebAppService.Install(
                        selectedInstallableWebApp);
                _ = AppJumpListService.RefreshAsync();

                BrowserNoticeService.Show(
                    $"{installed.Name} was installed.",
                    "success");

                var next =
                    new Dictionary<string, InstallableWebApp>(
                        installableWebApps.Value);

                next.Remove(selectedTag);

             
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show(
                    $"Could not install this app: {ex.Message}");
            }
        }
        void SetInstallableWebAppFromCore(
            string tabId,
            InstallableWebApp? app)
        {
            var next =
                new Dictionary<string, InstallableWebApp>(
                    installableWebApps.Value);

            if (app is null)
            {
                next.Remove(tabId);
            }
            else
            {
                next[tabId] = app;
            }

            installableWebApps.Set(next);
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

            var nextSection = section == CommandCenterSection.None
                ? string.Empty
                : sectionName;

            UpdateBrowserSession(state =>
            {
                var nextState = BrowserSessionStore.SetActiveCommandCenterSection(state, nextSection);

                return nextState;
            });

            switch (nextSection)
            {
                case nameof(CommandCenterSection.History):
                    RefreshHistoryState(busyText: "Loading history…");
                    break;
                case nameof(CommandCenterSection.Recent):
                    RefreshHistoryState(busyText: "Loading recent items…");
                    break;
                case nameof(CommandCenterSection.MostVisited):
                    RefreshHistoryState(busyText: "Loading most visited items…");
                    break;
                case nameof(CommandCenterSection.Favorites):
                    RefreshFavoritesState(busyText: "Loading favorites…");
                    break;
            }
        }

        void ImportBrowserHistoryByProfile(string browserName, string profileName)
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            var profileLabel = GetProfileLabel(historyImportBrowserProfiles.Value, browserName, profileName);

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.History)));
            var version = BeginCommandCenterWork($"Importing {browserName} history from {profileLabel}…");
            historyImportStatus.Set($"Importing {browserName} history from {profileLabel}…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserHistoryImportService.ImportBrowserHistory(browserName, profileName);
                    historyImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} items from {browserName} ({profileLabel})"
                        : $"No {browserName} history was imported from {profileLabel}.");
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    historyImportStatus.Set($"{browserName} history import failed for {profileLabel}.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void PulseCommandCenterHighlight(int durationMilliseconds = 1800)
        {
            var version = Interlocked.Increment(ref _commandCenterHighlightVersion);
            isCommandCenterHighlighted.Set(true);

            _ = Task.Run(async () =>
            {
                await Task.Delay(durationMilliseconds);

                if (version == Volatile.Read(ref _commandCenterHighlightVersion))
                {
                    isCommandCenterHighlighted.Set(false);
                }
            });
        }

        void ImportBrowserFavoritesByProfile(string browserName, string profileName)
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            var profileLabel = GetProfileLabel(favoritesImportBrowserProfiles.Value, browserName, profileName);

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.Favorites)));
            var version = BeginCommandCenterWork($"Importing {browserName} favorites from {profileLabel}…");
            favoritesImportStatus.Set($"Importing {browserName} favorites from {profileLabel}…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserFavoritesImportService.ImportBrowserFavorites(browserName, profileName);
                    favoritesImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} favorites from {browserName} ({profileLabel})"
                        : $"No {browserName} favorites were imported from {profileLabel}.");
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    favoritesImportStatus.Set($"{browserName} favorites import failed for {profileLabel}.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ClearCommandCenterStatuses()
        {
            historyImportStatus.Set(string.Empty);
            favoritesImportStatus.Set(string.Empty);
            collectionStatus.Set(string.Empty);
        }

        void DismissCommandCenter()
        {
            ClearCommandCenterStatuses();
            UpdateBrowserSession(BrowserSessionStore.DismissCommandCenter);
        }

        void ToggleChatBlade()
        {
            UpdateBrowserSession(state => BrowserSessionStore.SetChatOpen(state, !state.IsChatOpen));
        }

        void CloseChatBlade()
        {
            UpdateBrowserSession(state => BrowserSessionStore.SetChatOpen(state, false));
        }

        void ToggleLinkerCompact()
        {
            isLinkerCompactState.Set(!isLinkerCompactState.Value);
        }

        void CompactCommandCenterForBrowsing()
        {
            UpdateBrowserSession(BrowserSessionStore.CompactCommandCenterForBrowsing);
        }

        void ToggleCommandCenterExpanded()
        {
            if (!isCommandCenterOpen)
            {
                return;
            }

            var nextExpanded = !isCommandCenterExpanded;
            var nextSession = BrowserSessionStore.SetCommandCenterExpanded(session.Value, nextExpanded);

            if (nextExpanded)
            {
                nextSession = BrowserSessionStore.SetTabsCollapsed(nextSession, false);
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

            ClearCommandCenterStatuses();
            ToggleCommandCenter(section);

            if (section == CommandCenterSection.Collections)
            {
                RefreshCollectionState();
            }
        }

        void OpenCollectionsExpanded()
        {
            UpdateBrowserSession(state =>
            {
                var nextState = BrowserSessionStore.SetTabsCollapsed(state, false);
                nextState = BrowserSessionStore.SetActiveCommandCenterSection(nextState, nameof(CommandCenterSection.Collections));
                nextState = BrowserSessionStore.SetCommandCenterExpanded(nextState, true);
                return BrowserSessionStore.SetRailTabsExpanded(nextState, false);
            });

            RefreshCollectionState();
        }

        void SetDefaultSearchProvider(string providerKey)
        {
            var normalizedProviderKey = BrowserSearchProviders.NormalizeProviderKey(providerKey);
            SettingsService.SetValue(DefaultSearchProviderSettingKey, normalizedProviderKey);
            _ = AppJumpListService.RefreshAsync();
            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedSearchProvider(state, normalizedProviderKey));
            settingsSnapshot.Set(SettingsService.Dump());
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
            settingsSnapshot.Set(SettingsService.Dump());

            if (string.Equals(key, PasswordAutosaveService.SettingKey, StringComparison.Ordinal) &&
                bool.TryParse(value, out var passwordAutosaveEnabled))
            {
                _browserWebViewHostController.SetPasswordAutosaveEnabled(passwordAutosaveEnabled);
            }

            if (string.Equals(key, FirstRunExperienceService.SettingKey, StringComparison.Ordinal) &&
                string.Equals(value, FirstRunExperienceService.PendingValue, StringComparison.OrdinalIgnoreCase))
            {
                isFirstRunSetupVisible.Set(true);
            }

            if (string.Equals(key, DefaultSearchProviderSettingKey, StringComparison.Ordinal))
            {
                UpdateBrowserSession(state => BrowserSessionStore.SetSelectedSearchProvider(
                    state,
                    BrowserSearchProviders.NormalizeProviderKey(value)));
            }

            if (string.Equals(key, SaveTabsSettingKey, StringComparison.Ordinal))
            {
                if (bool.TryParse(value, out var saveTabsEnabled) && !saveTabsEnabled)
                {
                    _saveTabsCts?.Cancel();
                    _saveTabsCts?.Dispose();
                    _saveTabsCts = null;
                    ClearPersistedStartupTabs();
                }
                else
                {
                    ScheduleTabsSave(_latestTabs.Length > 0 ? _latestTabs : tabs, _latestSelectedTabId ?? selectedTag);
                }
            }
        }

        async Task<FirstRunImportResult> ImportFirstRunDataAsync(
            IReadOnlyList<FirstRunProfileSelection> profiles,
            bool importFavorites,
            bool importHistory)
        {
            var favoriteCount = 0;
            var historyCount = 0;
            var sourceCount = 0;
            var errorCount = 0;

            firstRunImportNotice.Set(new FirstRunImportNotice(
                $"{profiles.Count} selected profile{(profiles.Count == 1 ? "" : "s")}",
                IsBusy: true,
                HasErrors: false));
            isFirstRunImportNoticeVisible.Set(true);

            foreach (var profile in profiles.DistinctBy(
                item => $"{item.BrowserName}\u001F{item.ProfileId}",
                StringComparer.OrdinalIgnoreCase))
            {
                if (importFavorites)
                {
                    try
                    {
                        var summary = await Task.Run(
                            () => BrowserFavoritesImportService.ImportBrowserFavorites(
                                profile.BrowserName,
                                profile.ProfileId));
                        favoriteCount += summary.ImportedItemCount;
                        sourceCount += summary.SourceCount;
                    }
                    catch
                    {
                        errorCount++;
                    }
                }

                if (importHistory)
                {
                    try
                    {
                        var summary = await Task.Run(
                            () => BrowserHistoryImportService.ImportBrowserHistory(
                                profile.BrowserName,
                                profile.ProfileId));
                        historyCount += summary.ImportedItemCount;
                        sourceCount += summary.SourceCount;
                    }
                    catch
                    {
                        errorCount++;
                    }
                }
            }

            if (importFavorites)
            {
                try
                {
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    errorCount++;
                }
            }

            if (importHistory)
            {
                try
                {
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    errorCount++;
                }
            }

            var importedCount = favoriteCount + historyCount;
            firstRunImportNotice.Set(new FirstRunImportNotice(
                errorCount == 0
                    ? $"{importedCount:N0} items · {sourceCount} source{(sourceCount == 1 ? "" : "s")}"
                    : $"{importedCount:N0} items · {errorCount} source{(errorCount == 1 ? "" : "s")} failed",
                IsBusy: false,
                HasErrors: errorCount > 0));

            return new FirstRunImportResult(
                favoriteCount,
                historyCount,
                sourceCount);
        }

        void CompleteFirstRunSetup()
        {
            FirstRunExperienceService.Complete();
            settingsSnapshot.Set(SettingsService.Dump());
            isFirstRunSetupVisible.Set(false);
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

        async void ShowLinkerProviderKeyDialog()
        {
            var xamlRoot = global::LinkScape.Application.MainWindowActivation.GetXamlRoot();
            if (xamlRoot is null)
            {
                BrowserNoticeService.Show("Linker cannot open the key dialog until the main window is ready.");
                return;
            }

            var providers = LinkerAiCredentialService.Providers;
            var selectedProvider = LinkerAiCredentialService.SelectedProvider;
            var providerPicker = new ComboBox
            {
                Header = "Provider",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            for (var index = 0; index < providers.Count; index++)
            {
                var provider = providers[index];
                providerPicker.Items.Add(new ComboBoxItem
                {
                    Content = provider.DisplayName,
                    Tag = provider.Id
                });

                if (string.Equals(provider.Id, selectedProvider.Id, StringComparison.OrdinalIgnoreCase))
                {
                    providerPicker.SelectedIndex = index;
                }
            }

            if (providerPicker.SelectedIndex < 0)
            {
                providerPicker.SelectedIndex = 0;
            }

            var passwordBox = new PasswordBox
            {
                Header = "API key",
                PlaceholderText = "Paste your provider key",
                PasswordRevealMode = PasswordRevealMode.Peek
            };

            var endpointBox = new TextBox
            {
                Header = "Endpoint",
                Text = LinkerAiCredentialService.GetConfiguredEndpoint(selectedProvider.Id),
                PlaceholderText = selectedProvider.EndpointPlaceholder,
                Visibility = selectedProvider.RequiresEndpoint ? Visibility.Visible : Visibility.Collapsed
            };

            var deploymentBox = new TextBox
            {
                Header = "Deployment / bot",
                Text = LinkerAiCredentialService.GetConfiguredDeployment(selectedProvider.Id),
                PlaceholderText = selectedProvider.DeploymentPlaceholder,
                Visibility = selectedProvider.RequiresDeployment ? Visibility.Visible : Visibility.Collapsed
            };

            var description = new TextBlock
            {
                Text = selectedProvider.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78
            };

            providerPicker.SelectionChanged += (_, _) =>
            {
                if (providerPicker.SelectedItem is not ComboBoxItem item ||
                    item.Tag is not string providerId)
                {
                    return;
                }

                var provider = LinkerAiCredentialService.GetProvider(providerId);
                description.Text = provider.Description;
                endpointBox.Text = LinkerAiCredentialService.GetConfiguredEndpoint(provider.Id);
                endpointBox.PlaceholderText = provider.EndpointPlaceholder;
                endpointBox.Visibility = provider.RequiresEndpoint ? Visibility.Visible : Visibility.Collapsed;
                deploymentBox.Text = LinkerAiCredentialService.GetConfiguredDeployment(provider.Id);
                deploymentBox.PlaceholderText = provider.DeploymentPlaceholder;
                deploymentBox.Visibility = provider.RequiresDeployment ? Visibility.Visible : Visibility.Collapsed;
            };

            var content = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new Image
                            {
                                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png")),
                                Width = 34,
                                Height = 34
                            },
                            new StackPanel
                            {
                                Spacing = 2,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "Linker provider key",
                                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                                    },
                                    new TextBlock
                                    {
                                        Text = "Local browser tools stay on device. A provider key lets Linker answer broader questions when local tools are not enough.",
                                        TextWrapping = TextWrapping.Wrap,
                                        Opacity = 0.72
                                    }
                                }
                            }
                        }
                    },
                    providerPicker,
                    description,
                    passwordBox,
                    endpointBox,
                    deploymentBox,
                    new TextBlock
                    {
                        Text = "The key is stored with Windows Credential Manager. LinkScape only keeps the selected provider and non-secret options in settings.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.68
                    }
                }
            };

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Connect Linker",
                Content = content,
                PrimaryButtonText = "Save & test",
                SecondaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
            {
                return;
            }

            if (providerPicker.SelectedItem is not ComboBoxItem selectedItem ||
                selectedItem.Tag is not string selectedProviderId)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(passwordBox.Password))
            {
                await ShowLinkerProviderResultDialogAsync("Key not saved", "Paste an API key before saving.");
                return;
            }

            LinkerAiCredentialService.SaveCredential(
                selectedProviderId,
                passwordBox.Password,
                endpointBox.Text,
                deploymentBox.Text);
            settingsSnapshot.Set(SettingsService.Dump());

            if (result == ContentDialogResult.Secondary)
            {
                BrowserNoticeService.Show($"{LinkerAiCredentialService.GetProvider(selectedProviderId).DisplayName} key saved for Linker.");
                return;
            }

            var testResult = await LinkerAiCredentialService.TestProviderAsync(selectedProviderId);
            await ShowLinkerProviderResultDialogAsync(
                testResult.Succeeded ? "Key works" : "Key test failed",
                testResult.Message);
            settingsSnapshot.Set(SettingsService.Dump());
        }

        void OpenUriInNewTab(string rawUrl, bool dismissCommandCenter = true)
        {
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

            if (dismissCommandCenter)
            {
                EnqueueUiTransition(DismissCommandCenter);
            }
        }

        void OpenSavedTabsActivation()
        {
            _suppressTabPersistence = false;
            var nextTabs = LoadSavedTabs();
            var nextSelected = ResolveStartupSelectedTab(nextTabs);
            MarkTabsChanged(nextTabs);
            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, nextSelected.Id));
            _browserTitleBarController.SetAddressText(nextSelected.Url);
        }

        void OpenSearchActivation()
        {
            SuppressTabPersistence();
            var defaultProviderKey = BrowserSearchProviders.NormalizeProviderKey(
                SettingsService.GetValueOrDefault(
                    DefaultSearchProviderSettingKey,
                    BrowserSearchProviders.DefaultProviderKey));
            var defaultTab = BrowserTab.CreateHome(BrowserSearchProviders.GetHomeUrl(defaultProviderKey));
            var nextTabs = new[] { defaultTab };
            MarkTabsChanged(nextTabs);
            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, defaultTab.Id));
            _browserTitleBarController.SetAddressText(defaultTab.Url);
        }

        void OpenCollectionActivation(string collectionId, bool append, bool stop)
        {
            try
            {
                if (stop)
                {
                    var stoppedCollection = TabCollectionService.GetCollection(collectionId);
                    if (stoppedCollection is null)
                    {
                        BrowserNoticeService.Show("That collection is no longer available.");
                        return;
                    }

                    var collectionUrls = TabCollectionService.GetItems(stoppedCollection.Id)
                        .Select(item => item.Url)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
                    var remainingTabs = currentTabs
                        .Where(tab => !collectionUrls.Contains(tab.Url))
                        .ToArray();
                    var closedCount = currentTabs.Length - remainingTabs.Length;
                    if (closedCount == 0)
                    {
                        var inactiveMessage = $"'{stoppedCollection.Name}' is not running.";
                        collectionStatus.Set(inactiveMessage);
                        BrowserNoticeService.Show(inactiveMessage);
                        return;
                    }

                    if (remainingTabs.Length == 0)
                    {
                        remainingTabs = [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
                    }

                    remainingTabs = remainingTabs
                        .Select((tab, index) => tab with { Order = index })
                        .ToArray();
                    var selectedTabId = remainingTabs.Any(tab =>
                        string.Equals(tab.Id, _latestSelectedTabId ?? selectedTag, StringComparison.Ordinal))
                            ? _latestSelectedTabId ?? selectedTag
                            : remainingTabs[0].Id;
                    MarkTabsChanged(remainingTabs);
                    UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, selectedTabId));
                    _browserTitleBarController.SetAddressText(
                        remainingTabs.First(tab => string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal)).Url);
                    ScheduleTabsSave(remainingTabs, selectedTabId);
                    collectionName.Set(stoppedCollection.Name);
                    var stoppedMessage = $"Stopped '{stoppedCollection.Name}' - closed {closedCount} pages.";
                    collectionStatus.Set(stoppedMessage);
                    BrowserNoticeService.Show(stoppedMessage);
                    RefreshCollectionState(stoppedCollection.Name);
                    return;
                }

                if (append)
                {
                    var appendedCollection = TabCollectionService.GetCollection(collectionId);
                    if (appendedCollection is null)
                    {
                        const string message = "That collection is no longer available.";
                        collectionStatus.Set(message);
                        BrowserNoticeService.Show(message);
                        return;
                    }

                    var appendedTabs = TabCollectionService.GetItems(appendedCollection.Id)
                        .Take(MaxTabs)
                        .Select((item, index) => BrowserTab.CreateNew(index + 1, item.Url) with
                        {
                            Title = item.Title,
                            Order = index
                        })
                        .ToArray();
                    if (appendedTabs.Length == 0)
                    {
                        var message = $"'{appendedCollection.Name}' does not contain any pages yet.";
                        collectionStatus.Set(message);
                        BrowserNoticeService.Show(message);
                        return;
                    }

                    var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
                    var existingUrls = currentTabs
                        .Select(tab => tab.Url)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var newCollectionTabs = appendedTabs
                        .Where(tab => existingUrls.Add(tab.Url))
                        .ToArray();
                    if (newCollectionTabs.Length == 0)
                    {
                        var message = $"Running '{appendedCollection.Name}' - all {appendedTabs.Length} pages are active.";
                        collectionStatus.Set(message);
                        BrowserNoticeService.Show(message);
                        return;
                    }

                    var appendedSessionTabs = AppendImportedTabs(currentTabs, newCollectionTabs);
                    var addedCount = appendedSessionTabs.Length - currentTabs.Length;
                    if (addedCount == 0)
                    {
                        var message = $"Close a tab before starting '{appendedCollection.Name}'.";
                        collectionStatus.Set(message);
                        BrowserNoticeService.Show(message);
                        return;
                    }

                    var selectedTabId = _latestSelectedTabId ?? selectedTag;
                    MarkTabsChanged(appendedSessionTabs);
                    UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, selectedTabId));
                    ScheduleTabsSave(appendedSessionTabs, selectedTabId);
                    collectionName.Set(appendedCollection.Name);
                    var activePageCount = currentTabs.Count(tab =>
                        appendedTabs.Any(collectionTab =>
                            string.Equals(collectionTab.Url, tab.Url, StringComparison.OrdinalIgnoreCase))) + addedCount;
                    var successMessage = $"Running '{appendedCollection.Name}' - {activePageCount} pages active.";
                    collectionStatus.Set(successMessage);
                    BrowserNoticeService.Show(successMessage);
                    RefreshCollectionState(appendedCollection.Name);
                    return;
                }

                _suppressTabPersistence = false;
                var nextTabs = SetAndLoadStartupCollection(collectionId);
                var nextSelected = nextTabs[0];
                MarkTabsChanged(nextTabs);
                UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, nextSelected.Id));
                _browserTitleBarController.SetAddressText(nextSelected.Url);

                var collection = TabCollectionService.GetCollection(collectionId);
                if (collection is not null)
                {
                    collectionName.Set(collection.Name);
                    collectionStatus.Set($"Opened '{collection.Name}' and set it for startup.");
                    RefreshCollectionState(collection.Name);
                }

                settingsSnapshot.Set(SettingsService.Dump());
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not open that collection: {ex.Message}");
            }
        }

        void OpenActiveTabsPackageActivation(string packageJson)
        {
            if (!ActiveTabsPackage.TryParse(packageJson, out var package, out var error))
            {
                collectionStatus.Set(error);
                return;
            }

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
            var packageTabs = CreateTabsFromPackage(package, selectedSearchProviderKey, out var packageSelectedTabId);
            var nextTabs = package.ShouldAppend
                ? AppendImportedTabs(currentTabs, packageTabs)
                : packageTabs;
            var nextSelectedTabId = package.ShouldAppend && package.SelectedIndex is null && string.IsNullOrWhiteSpace(package.SelectedTabId)
                ? _latestSelectedTabId ?? selectedTag
                : packageSelectedTabId;

            if (!nextTabs.Any(tab => string.Equals(tab.Id, nextSelectedTabId, StringComparison.Ordinal)))
            {
                nextSelectedTabId = nextTabs[0].Id;
            }

            if (!package.ShouldSaveState)
            {
                SuppressTabPersistence();
            }

            SavePackageCollection(package);
            MarkTabsChanged(nextTabs);
            UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, nextSelectedTabId));
            _browserTitleBarController.SetAddressText(nextTabs.First(tab => tab.Id == nextSelectedTabId).Url);
            ScheduleTabsSave(nextTabs, nextSelectedTabId);
        }

        void OpenActivationTarget(ActivationTarget target)
        {
            switch (target.Kind)
            {
                case ActivationTargetKind.Url:
                    OpenUriInNewTab(target.Value, dismissCommandCenter: false);
                    break;
                case ActivationTargetKind.InstalledApp:
                    WebAppWindowService.TryOpenById(target.Value);
                    break;
                case ActivationTargetKind.Collection:
                    OpenCollectionActivation(target.Value, target.ShouldAppend, target.ShouldStop);
                    break;
                case ActivationTargetKind.ActiveTabsPackage:
                    OpenActiveTabsPackageActivation(target.Value);
                    break;
                case ActivationTargetKind.Collections:
                    OpenCollectionsExpanded();
                    break;
                case ActivationTargetKind.SavedTabs:
                    OpenSavedTabsActivation();
                    break;
                case ActivationTargetKind.Search:
                    OpenSearchActivation();
                    break;
                case ActivationTargetKind.ShareTarget when target.ShareOperation is not null:
                    _ = WindowsShareTargetService.ShowImagePreviewAsync(
                        target.ShareOperation,
                        url => OpenUriInNewTab(url, dismissCommandCenter: false));
                    break;
                case ActivationTargetKind.MainBrowser:
                    break;
            }
        }

        _openActivatedTarget = OpenActivationTarget;
        RegisterActivationListener();

        if (!_deferredStartupActivationQueued && _deferredStartupActivation is { } deferredActivation)
        {
            _deferredStartupActivationQueued = true;
            EnqueueUiTransition(() => OpenActivationTarget(deferredActivation));
        }

        if (!_deferredWhatsNewQueued && !string.IsNullOrWhiteSpace(_deferredWhatsNewVersion))
        {
            _deferredWhatsNewQueued = true;
            var version = _deferredWhatsNewVersion;
            EnqueueUiTransition(() =>
            {
                var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
                if (currentTabs.Length >= MaxTabs)
                {
                    _deferredWhatsNewQueued = false;
                    return;
                }

                OpenUriInNewTab(AppUpdateService.GetWhatsNewPageUrl(version), dismissCommandCenter: false);
                AppUpdateService.MarkPackageVersionSeen(version);
                _deferredWhatsNewVersion = null;
            });
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
            _commandCenterBusyStartedAtUtc = DateTime.UtcNow;
            isCommandCenterBusy.Set(true);
            commandCenterBusyText.Set(busyText);
            return version;
        }

        void EndCommandCenterWork(int version)
        {
            if (version != Volatile.Read(ref _commandCenterBusyVersion))
            {
                return;
            }

            var elapsed = DateTime.UtcNow - _commandCenterBusyStartedAtUtc;
            var remaining = TimeSpan.FromMilliseconds(CommandCenterBusyMinimumDurationMilliseconds) - elapsed;

            if (remaining > TimeSpan.Zero)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(remaining);

                    if (version != Volatile.Read(ref _commandCenterBusyVersion))
                    {
                        return;
                    }

                    commandCenterBusyText.Set(string.Empty);
                    isCommandCenterBusy.Set(false);
                });

                return;
            }

            commandCenterBusyText.Set(string.Empty);
            isCommandCenterBusy.Set(false);
        }

        void SetHistoryStateFromDatabase(string? filterOverride = null)
        {
            var effectiveFilter = filterOverride ?? historyFilter.Value;
            recentHistory.Set(LoadRecentHistoryItems(effectiveFilter, historyLimit.Value));
            mostVisitedHistory.Set(LoadMostVisitedHistoryItems());
        }

        void RefreshHistoryState(string? filterOverride = null, string busyText = "Loading history…")
        {
            var version = BeginCommandCenterWork(busyText);

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
            historyFilter.Set(nextFilter);
            historyLimit.Set(50);
            _historyFilterCts?.Cancel();
            _historyFilterCts?.Dispose();
            var cts = _historyFilterCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FilterDebounceMilliseconds, cts.Token);
                    var results = LoadRecentHistoryItems(nextFilter, 50);

                    if (!cts.IsCancellationRequested)
                    {
                        recentHistory.Set(results);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        void LoadMoreHistory()
        {
            var nextLimit = Math.Min(historyLimit.Value + 100, 2500);
            historyLimit.Set(nextLimit);
            recentHistory.Set(LoadRecentHistoryItems(historyFilter.Value, nextLimit));
        }

        void SetFavoritesStateFromDatabase(string? filterOverride = null)
        {
            var effectiveFilter = filterOverride ?? favoritesFilter.Value;
            favoriteItems.Set(LoadFavoriteItems(effectiveFilter, favoritesLimit.Value));
        }

        void RefreshFavoritesState(string? filterOverride = null, string busyText = "Loading favorites…")
        {
            var version = BeginCommandCenterWork(busyText);

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
            favoritesFilter.Set(nextFilter);
            favoritesLimit.Set(InitialFavoriteQueryLimit);
            _favoritesFilterCts?.Cancel();
            _favoritesFilterCts?.Dispose();
            var cts = _favoritesFilterCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FilterDebounceMilliseconds, cts.Token);
                    var results = LoadFavoriteItems(nextFilter, InitialFavoriteQueryLimit);

                    if (!cts.IsCancellationRequested)
                    {
                        favoriteItems.Set(results);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        void RefreshVisibleHistoryAfterNavigation()
        {
            if (!string.Equals(activeCommandCenterSection, nameof(CommandCenterSection.History), StringComparison.Ordinal) &&
                !string.Equals(activeCommandCenterSection, nameof(CommandCenterSection.Recent), StringComparison.Ordinal) &&
                !string.Equals(activeCommandCenterSection, nameof(CommandCenterSection.MostVisited), StringComparison.Ordinal))
            {
                return;
            }

            _ = Task.Run(() => SetHistoryStateFromDatabase());
        }

        void LoadMoreFavorites()
        {
            var nextLimit = Math.Min(favoritesLimit.Value + 150, 2500);
            favoritesLimit.Set(nextLimit);
            favoriteItems.Set(LoadFavoriteItems(favoritesFilter.Value, nextLimit));
        }

        void SetCollectionStateFromDatabase(string? collectionNameOverride = null)
        {
            var collections = TabCollectionService.GetCollections().ToArray();
            var requestedName = collectionNameOverride ?? collectionName.Value;
            var effectiveCollection = collections.FirstOrDefault(collection =>
                string.Equals(collection.Name, requestedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(collection.Id, requestedName, StringComparison.Ordinal));

            if (effectiveCollection is null)
            {
                effectiveCollection = collections.FirstOrDefault();
            }

            var effectiveName = effectiveCollection?.Name ?? "Personal";
            tabCollections.Set(collections);
            collectionName.Set(effectiveName);
            collectionItems.Set(effectiveCollection is null
                ? []
                : TabCollectionService.GetItems(effectiveCollection.Id).ToArray());
            collectionMembership.Set(BuildCollectionMembership(collections));
        }

        void RefreshCollectionState(string? collectionNameOverride = null, string busyText = "Loading collections...")
        {
            var version = BeginCommandCenterWork(busyText);

            _ = Task.Run(() =>
            {
                try
                {
                    SetCollectionStateFromDatabase(collectionNameOverride);
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        if (!_collectionStateLoadStarted)
        {
            _collectionStateLoadStarted = true;
            _ = Task.Run(() => SetCollectionStateFromDatabase());
        }

        void ApplyCollectionName(string nextName)
        {
            collectionName.Set(nextName);
            collectionItems.Set(TabCollectionService.GetItems(nextName).ToArray());
        }

        void CreateCollection()
        {
            try
            {
                var collection = TabCollectionService.UpsertCollection(collectionName.Value);
                _ = AppJumpListService.RefreshAsync();
                collectionStatus.Set($"Collection '{collection.Name}' is ready.");
                RefreshCollectionState(collection.Name);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void CreateSmartCollections()
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            var version = BeginCommandCenterWork("Creating Smart Collections...");
            collectionStatus.Set("Creating Smart Collections from History and Favorites...");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = SmartCollectionService.CreateOrRefresh();
                    var firstCollection = summary.CollectionItemCounts
                        .Where(item => item.Value > 0)
                        .Select(item => item.Key)
                        .FirstOrDefault();
                    collectionStatus.Set(
                        $"Smart Collections refreshed: {summary.ItemCount} matched items from History and Favorites.");
                    _ = AppJumpListService.RefreshAsync();
                    SetCollectionStateFromDatabase(firstCollection);
                }
                catch (Exception ex)
                {
                    collectionStatus.Set($"Smart Collections failed: {ex.Message}");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        async void DeleteSelectedCollection()
        {
            var selectedCollection = TabCollectionService.GetCollectionByName(collectionName.Value);
            if (selectedCollection is null)
            {
                collectionStatus.Set("Select an existing collection to delete.");
                return;
            }

            var itemCount = TabCollectionService.GetItems(selectedCollection.Id).Count;
            var itemLabel = itemCount == 1 ? "saved site" : "saved sites";
            var confirmed = await ConfirmDestructiveActionAsync(
                $"Delete '{selectedCollection.Name}'?",
                $"This permanently removes the collection and its {itemCount} {itemLabel}. Open tabs, history, and favorites are not affected.",
                "Delete collection");

            if (!confirmed)
            {
                return;
            }

            try
            {
                if (!TabCollectionService.DeleteCollection(selectedCollection.Id))
                {
                    collectionStatus.Set("The selected collection could not be found.");
                    return;
                }

                try
                {
                    CollectionShortcutService.Remove(selectedCollection.Id);
                }
                catch
                {
                    // The collection is deleted even if Windows cannot remove its optional desktop launcher.
                }

                _ = AppJumpListService.RefreshAsync();
                settingsSnapshot.Set(SettingsService.Dump());
                var nextCollectionName = TabCollectionService.GetCollections().FirstOrDefault()?.Name ?? "Personal";
                collectionStatus.Set($"Deleted collection '{selectedCollection.Name}'.");
                RefreshCollectionState(nextCollectionName);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void AddCurrentTabToCollection()
        {
            try
            {
                var selectedTab = (_latestTabs.Length > 0 ? _latestTabs : tabs)
                    .FirstOrDefault(tab => string.Equals(tab.Id, selectedTag, StringComparison.Ordinal));

                if (selectedTab is null)
                {
                    collectionStatus.Set("No active tab is available.");
                    return;
                }

                var item = TabCollectionService.AddOrUpdateItem(collectionName.Value, selectedTab.Url, selectedTab.Title);
                _ = AppJumpListService.RefreshAsync();
                collectionStatus.Set($"Added '{item.Title}' to {collectionName.Value}.");
                RefreshCollectionState(collectionName.Value);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void AddUrlToCollection(string targetCollectionName, string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                var safeTitle = string.IsNullOrWhiteSpace(title) ? url : title;
                var item = TabCollectionService.AddOrUpdateItem(targetCollectionName, url, safeTitle);
                _ = AppJumpListService.RefreshAsync();
                var collection = TabCollectionService.GetCollection(item.CollectionId);
                var resolvedCollectionName = collection?.Name ?? targetCollectionName;

                collectionStatus.Set($"Added '{item.Title}' to {resolvedCollectionName}.");
                RefreshCollectionState(collectionName.Value);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void SetStartupCollection()
        {
            try
            {
                TabCollectionService.SetStartupCollection(collectionName.Value);
                collectionStatus.Set($"LinkScape will open '{collectionName.Value}' on startup.");
                settingsSnapshot.Set(SettingsService.Dump());
                RefreshCollectionState(collectionName.Value);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void ImportBrowserHistory()
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.History)));
            var version = BeginCommandCenterWork("Importing history…");
            historyImportStatus.Set("Importing browser history…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserHistoryImportService.ImportAllHistory();
                    historyImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} items from {summary.SourceCount} sources"
                        : "No supported browser history sources were found.");
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    historyImportStatus.Set("Browser history import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ImportBrowserHistoryByName(string browserName)
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.History)));
            var version = BeginCommandCenterWork($"Importing {browserName} history…");
            historyImportStatus.Set($"Importing {browserName} history…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserHistoryImportService.ImportBrowserHistory(browserName);
                    historyImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} items from {browserName}"
                        : $"No {browserName} history was imported.");
                    SetHistoryStateFromDatabase();
                }
                catch
                {
                    historyImportStatus.Set($"{browserName} history import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        async Task<bool> TryClearBrowserDataAsync(
            Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds dataKinds,
            string successMessage)
        {
            try
            {
                await _browserWebViewHostController.ClearBrowsingDataAsync(dataKinds);
                BrowserNoticeService.Show(successMessage);
                return true;
            }
            catch (Exception ex)
            {
                BrowserNoticeService.Show($"Could not clear browsing data: {ex.Message}");
                return false;
            }
        }

        async void ClearBrowserCache()
        {
            await TryClearBrowserDataAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache,
                "Cached browser files cleared.");
        }

        async void ClearBrowserCookies()
        {
            var confirmed = await ConfirmDestructiveActionAsync(
                "Clear all cookies?",
                "This signs you out of websites in LinkScape.",
                "Clear cookies");

            if (confirmed)
            {
                await TryClearBrowserDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.Cookies,
                    "Browser cookies cleared.");
            }
        }

        async void ClearCoreBrowsingHistory()
        {
            var confirmed = await ConfirmDestructiveActionAsync(
                "Clear browser engine history?",
                "This removes navigation history maintained by the browser engine. LinkScape's visible history is not changed.",
                "Clear browser history");

            if (confirmed)
            {
                await TryClearBrowserDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.BrowsingHistory,
                    "Browser engine history cleared.");
            }
        }

        async void DeleteAllHistory()
        {
            var confirmed = await ConfirmDestructiveActionAsync(
                "Delete all history?",
                "This permanently removes LinkScape history and the browser engine's navigation history.",
                "Delete history");

            if (!confirmed)
            {
                return;
            }

            var coreHistoryCleared = await TryClearBrowserDataAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.BrowsingHistory,
                "Browser engine history cleared.");
            if (!coreHistoryCleared)
            {
                return;
            }

            HistoryPersistenceService.ClearHistory();
            historyImportStatus.Set("Deleted all history.");
            RefreshHistoryState();
        }

        void ImportBrowserFavorites()
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.Favorites)));
            var version = BeginCommandCenterWork("Importing favorites…");
            favoritesImportStatus.Set("Importing browser favorites…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserFavoritesImportService.ImportAllFavorites();
                    favoritesImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} favorites from {summary.SourceCount} sources"
                        : "No supported browser favorites were found.");
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    favoritesImportStatus.Set("Browser favorites import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        void ImportBrowserFavoritesByName(string browserName)
        {
            if (isCommandCenterBusy.Value)
            {
                return;
            }

            UpdateBrowserSession(state => BrowserSessionStore.SetActiveCommandCenterSection(state, nameof(CommandCenterSection.Favorites)));
            var version = BeginCommandCenterWork($"Importing {browserName} favorites…");
            favoritesImportStatus.Set($"Importing {browserName} favorites…");

            _ = Task.Run(() =>
            {
                try
                {
                    var summary = BrowserFavoritesImportService.ImportBrowserFavorites(browserName);
                    favoritesImportStatus.Set(summary.SourceCount > 0
                        ? $"Imported {summary.ImportedItemCount} favorites from {browserName}"
                        : $"No {browserName} favorites were imported.");
                    SetFavoritesStateFromDatabase();
                }
                catch
                {
                    favoritesImportStatus.Set($"{browserName} favorites import failed.");
                }
                finally
                {
                    EndCommandCenterWork(version);
                }
            });
        }

        async void DeleteAllFavorites()
        {
            var confirmed = await ConfirmDestructiveActionAsync(
                "Delete all favorites?",
                "This permanently removes all saved favorites from LinkScape and clears favorite markers from open tabs.",
                "Delete favorites");

            if (!confirmed)
            {
                return;
            }

            FavoritesService.ClearFavorites();
            var nextTabs = tabs
                .Select(tab => tab with
                {
                    FavoriteId = string.Empty,
                    IsFavorite = false
                })
                .ToArray();

            favoritesImportStatus.Set("Deleted all favorites.");
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

        void OpenCollectionItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            NavigateActiveTab(url);
        }

        void OpenCollectionItemInNewTab(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            OpenUriInNewTab(url, dismissCommandCenter: false);
        }

        void DeleteHistoryItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (isCommandCenterBusy.Value)
            {
                return;
            }

            PulseCommandCenterHighlight(CommandCenterBusyMinimumDurationMilliseconds);
            historyImportStatus.Set("Deleting history item…");
            recentHistory.Set(recentHistory.Value.Where(item => !string.Equals(item.Url, url, StringComparison.Ordinal)).ToArray());
            mostVisitedHistory.Set(mostVisitedHistory.Value.Where(item => !string.Equals(item.Url, url, StringComparison.Ordinal)).ToArray());

            _ = Task.Run(() =>
            {
                try
                {
                    HistoryPersistenceService.DeleteUrl(url);
                    historyImportStatus.Set("Deleted history item.");
                }
                catch
                {
                    historyImportStatus.Set("Deleting history item failed.");
                }
            });
        }

        void DeleteFavoriteItem(string favoriteId)
        {
            if (string.IsNullOrWhiteSpace(favoriteId))
            {
                return;
            }

            if (isCommandCenterBusy.Value)
            {
                return;
            }

            PulseCommandCenterHighlight(CommandCenterBusyMinimumDurationMilliseconds);
            favoritesImportStatus.Set("Removing favorite…");
            favoriteItems.Set(favoriteItems.Value.Where(item => !string.Equals(item.Id, favoriteId, StringComparison.Ordinal)).ToArray());

            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
            var changed = false;
            var nextTabs = currentTabs
                .Select(tab =>
                {
                    if (!string.Equals(tab.FavoriteId, favoriteId, StringComparison.Ordinal))
                    {
                        return tab;
                    }

                    changed = true;
                    return tab with
                    {
                        FavoriteId = string.Empty,
                        IsFavorite = false,
                        DateTime = DateTime.Now
                    };
                })
                .ToArray();

            if (changed)
            {
                MarkTabsChanged(nextTabs);
            }

            _ = Task.Run(() =>
            {
                try
                {
                    FavoritesService.RemoveFavorite(favoriteId);

                    favoritesImportStatus.Set("Removed favorite.");
                }
                catch
                {
                    favoritesImportStatus.Set("Removing favorite failed.");
                }
            });
        }

        void RemoveCollectionItem(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                var currentItem = collectionItems.Value.FirstOrDefault(item =>
                    string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
                var targetCollectionId = currentItem?.CollectionId ?? collectionName.Value;

                if (TabCollectionService.RemoveItem(targetCollectionId, url))
                {
                    _ = AppJumpListService.RefreshAsync();
                    collectionStatus.Set("Removed item from collection.");
                    RefreshCollectionState(TabCollectionService.GetCollection(targetCollectionId)?.Name);
                    return;
                }

                collectionStatus.Set("That item was already removed.");
                RefreshCollectionState(collectionName.Value);
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
        }

        void MoveCollectionItem(string itemId, int targetIndex)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            try
            {
                if (TabCollectionService.MoveItem(collectionName.Value, itemId, targetIndex))
                {
                    collectionStatus.Set("Moved collection item.");
                    RefreshCollectionState(collectionName.Value);
                }
            }
            catch (Exception ex)
            {
                collectionStatus.Set(ex.Message);
            }
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
            var openDifferentDomainInNewTab = settingsSnapshot.Value.TryGetValue(
                BrowserConstants.AddressBarOpenDifferentDomainInNewTabSettingKey,
                out var openDifferentDomainValue) &&
                bool.TryParse(openDifferentDomainValue, out var isEnabled) &&
                isEnabled;

            if (openDifferentDomainInNewTab &&
                !string.IsNullOrWhiteSpace(currentUrl) &&
                BrowserUrl.TryNormalizeAbsoluteUrl(rawUrl, out var normalizedAbsoluteTarget) &&
                !BrowserUrl.AreEqual(currentUrl, normalizedAbsoluteTarget) &&
                !BrowserUrl.IsSameDomain(currentUrl, normalizedAbsoluteTarget))
            {
                OpenUriInNewTab(normalizedAbsoluteTarget, dismissCommandCenter: false);
                return;
            }

            NavigateActiveTab(target);
        }

        void AddTab()
        {
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
            EnqueueUiTransition(() => UpdateBrowserSession(BrowserSessionStore.MaximizeRailTabs));

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
            var preferredSelectedTabId = wasSelected
                ? GetLastActiveOpenTabId(tabId, currentTabs)
                : null;
            var nextTabs = BrowserTabActions.Close(
                currentTabs,
                tabId,
                configuredHomeUrl,
                preferredSelectedTabId,
                out var nextTab);

            if (nextTab is null)
            {
                return;
            }

            _browserWebViewHostController.CloseTab(tabId);
            ForgetClosedTab(tabId);
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

        void OpenTabInNewWindow(string tabId)
        {
            var targetTab = (_latestTabs.Length > 0 ? _latestTabs : tabs)
                .FirstOrDefault(tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal));

            if (targetTab is null)
            {
                return;
            }

            ActivationRoutingService.OpenUrlInNewWindow(targetTab.Url);
        }

        void MoveTab(string tabId, int targetIndex)
        {
            var currentTabs = (_latestTabs.Length > 0 ? _latestTabs : tabs).ToList();
            var currentIndex = currentTabs.FindIndex(tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal));
            if (currentIndex < 0)
            {
                return;
            }

            targetIndex = Math.Clamp(targetIndex, 0, currentTabs.Count - 1);
            if (currentIndex == targetIndex)
            {
                return;
            }

            var movingTab = currentTabs[currentIndex];
            currentTabs.RemoveAt(currentIndex);
            currentTabs.Insert(targetIndex, movingTab);

            var nextTabs = currentTabs
                .Select((tab, index) => tab with
                {
                    Order = index
                })
                .ToArray();

            MarkTabsChanged(nextTabs);
        }

        void OpenSelectedTabInNewWindow()
        {
            OpenTabInNewWindow(selectedTag);
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

        BrowserNavigationResult ExecuteNavigationCommand(BrowserNavigationCommand command)
        {
            var currentTabs = _latestTabs.Length > 0 ? _latestTabs : tabs;
            var arguments = command.Arguments;
            var requestedTabId = arguments.TryGetValue("tabId", out var tabId) ? tabId : _latestSelectedTabId;
            var targetTab = currentTabs.FirstOrDefault(tab => string.Equals(tab.Id, requestedTabId, StringComparison.Ordinal));

            BrowserNavigationResult RequireTargetTab()
            {
                return targetTab is null
                    ? new BrowserNavigationResult(false, "The requested browser tab was not found.")
                    : new BrowserNavigationResult(true, string.Empty);
            }

            switch (command.ToolName)
            {
                case BrowserNavigationToolNames.TabsList:
                    return new BrowserNavigationResult(
                        true,
                        string.Join('\n', currentTabs.Select(tab =>
                            $"{tab.Id} | {tab.Title} | {tab.Url} | selected={string.Equals(tab.Id, _latestSelectedTabId, StringComparison.Ordinal)} | sleeping={tab.IsSleeping}")));

                case BrowserNavigationToolNames.TabsFind:
                    if (!arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
                    {
                        return new BrowserNavigationResult(false, "A tab title or URL query is required.");
                    }

                    var matches = currentTabs
                        .Where(tab =>
                            tab.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            tab.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(tab => string.Equals(tab.Title, query, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(tab => tab.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(tab => tab.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(tab => tab.Url.StartsWith(query, StringComparison.OrdinalIgnoreCase));
                    return new BrowserNavigationResult(true, string.Join('\n', matches.Select(tab =>
                        $"{tab.Id} | {tab.Title} | {tab.Url} | selected={string.Equals(tab.Id, _latestSelectedTabId, StringComparison.Ordinal)} | sleeping={tab.IsSleeping}")));

                case BrowserNavigationToolNames.TabsActivate:
                    var activateResult = RequireTargetTab();
                    if (!activateResult.Succeeded)
                    {
                        return activateResult;
                    }

                    SelectTab(Array.FindIndex(currentTabs, tab => tab.Id == targetTab!.Id));
                    return new BrowserNavigationResult(true, $"Activated tab '{targetTab!.Title}'.");

                case BrowserNavigationToolNames.Navigate:
                    var navigateResult = RequireTargetTab();
                    if (!navigateResult.Succeeded || !arguments.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
                    {
                        return navigateResult.Succeeded
                            ? new BrowserNavigationResult(false, "A URL is required.")
                            : navigateResult;
                    }

                    var targetUrl = BrowserUrl.Normalize(url, targetTab!.Url, selectedSearchProviderKey);
                    UpdateTab(targetTab.Id, tab => tab with { Url = targetUrl, DateTime = DateTime.Now });
                    _browserWebViewHostController.Navigate(targetTab.Id, targetUrl);
                    return new BrowserNavigationResult(true, $"Navigating '{targetTab.Title}' to {targetUrl}.");

                case BrowserNavigationToolNames.GoBack:
                    var backResult = RequireTargetTab();
                    if (!backResult.Succeeded)
                    {
                        return backResult;
                    }

                    _browserWebViewHostController.GoBack(targetTab!.Id);
                    return new BrowserNavigationResult(true, $"Navigated back in '{targetTab.Title}'.");

                case BrowserNavigationToolNames.GoForward:
                    var forwardResult = RequireTargetTab();
                    if (!forwardResult.Succeeded)
                    {
                        return forwardResult;
                    }

                    _browserWebViewHostController.GoForward(targetTab!.Id);
                    return new BrowserNavigationResult(true, $"Navigated forward in '{targetTab.Title}'.");

                case BrowserNavigationToolNames.Reload:
                    var reloadResult = RequireTargetTab();
                    if (!reloadResult.Succeeded)
                    {
                        return reloadResult;
                    }

                    _browserWebViewHostController.ReloadTab(targetTab!.Id);
                    return new BrowserNavigationResult(true, $"Reloaded '{targetTab.Title}'.");

                case BrowserNavigationToolNames.GoHome:
                    var homeResult = RequireTargetTab();
                    if (!homeResult.Succeeded)
                    {
                        return homeResult;
                    }

                    var homeUrl = GetConfiguredHomeUrl(settingsSnapshot.Value);
                    UpdateTab(targetTab!.Id, tab => tab with { Url = homeUrl, DateTime = DateTime.Now });
                    _browserWebViewHostController.Navigate(targetTab.Id, homeUrl);
                    return new BrowserNavigationResult(true, $"Navigating '{targetTab.Title}' home.");

                case BrowserNavigationToolNames.HomeGet:
                    return new BrowserNavigationResult(true, GetConfiguredHomeUrl(settingsSnapshot.Value));

                case BrowserNavigationToolNames.HomeSet:
                    if (!arguments.TryGetValue("url", out var newHomeUrl) || string.IsNullOrWhiteSpace(newHomeUrl))
                    {
                        return new BrowserNavigationResult(false, "A home URL is required.");
                    }

                    SaveSettingValue(HomeUrlSettingKey, newHomeUrl);
                    return new BrowserNavigationResult(true, $"Home URL set to {GetConfiguredHomeUrl(settingsSnapshot.Value)}.");

                case BrowserNavigationToolNames.TabsOpen:
                    if (!arguments.TryGetValue("url", out var openUrl) || string.IsNullOrWhiteSpace(openUrl))
                    {
                        return new BrowserNavigationResult(false, "A URL is required.");
                    }

                    var select = !arguments.TryGetValue("select", out var selectValue) || !bool.TryParse(selectValue, out var shouldSelect) || shouldSelect;
                    var normalizedOpenUrl = BrowserUrl.Normalize(openUrl, configuredHomeUrl, selectedSearchProviderKey);
                    var nextTabs = BrowserTabActions.Add(currentTabs, normalizedOpenUrl, out var newTab, visitCount: 1);
                    MarkTabsChanged(nextTabs);
                    if (select)
                    {
                        UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, newTab.Id));
                        _browserTitleBarController.SetAddressText(newTab.Url);
                    }

                    ScheduleTabsSave(nextTabs, select ? newTab.Id : _latestSelectedTabId ?? selectedTag);
                    return new BrowserNavigationResult(true, $"Opened tab '{newTab.Title}' at {newTab.Url}.");

                case BrowserNavigationToolNames.TabsOpenPackage:
                    if (!arguments.TryGetValue("packageJson", out var packageJson) &&
                        !arguments.TryGetValue("tabsJson", out packageJson) &&
                        !arguments.TryGetValue("tabs", out packageJson))
                    {
                        return new BrowserNavigationResult(false, "An active tabs JSON package is required.");
                    }

                    if (!ActiveTabsPackage.TryParse(packageJson, out var package, out var packageError))
                    {
                        return new BrowserNavigationResult(false, packageError);
                    }

                    var packageTabs = CreateTabsFromPackage(package, selectedSearchProviderKey, out var packageSelectedTabId);
                    var packageNextTabs = package.ShouldAppend
                        ? AppendImportedTabs(currentTabs, packageTabs)
                        : packageTabs;
                    var packageNextSelectedTabId = package.ShouldAppend && package.SelectedIndex is null && string.IsNullOrWhiteSpace(package.SelectedTabId)
                        ? _latestSelectedTabId ?? selectedTag
                        : packageSelectedTabId;

                    if (!packageNextTabs.Any(tab => string.Equals(tab.Id, packageNextSelectedTabId, StringComparison.Ordinal)))
                    {
                        packageNextSelectedTabId = packageNextTabs[0].Id;
                    }

                    if (!package.ShouldSaveState)
                    {
                        SuppressTabPersistence();
                    }

                    SavePackageCollection(package);
                    MarkTabsChanged(packageNextTabs);
                    UpdateBrowserSession(state => BrowserSessionStore.SetSelectedTab(state, packageNextSelectedTabId));
                    _browserTitleBarController.SetAddressText(packageNextTabs.First(tab => tab.Id == packageNextSelectedTabId).Url);
                    ScheduleTabsSave(packageNextTabs, packageNextSelectedTabId);
                    return new BrowserNavigationResult(true, $"Opened {packageTabs.Length} active tab package item(s).");

                default:
                    return new BrowserNavigationResult(false, $"Unsupported browser navigation tool '{command.ToolName}'.");
            }
        }

        BrowserNavigationService.RegisterHandler(command =>
        {
            if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            {
                return ExecuteNavigationCommand(command);
            }

            var completion = new TaskCompletionSource<BrowserNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_dispatcherQueue.TryEnqueue(() => completion.SetResult(ExecuteNavigationCommand(command))))
            {
                return new BrowserNavigationResult(false, "The LinkScape UI thread is unavailable for browser navigation.");
            }

            return completion.Task.GetAwaiter().GetResult();
        });
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
                _browserWebViewHostController,
                selectedTab,
                tabs,
                configuredHomeUrl,
                settingsSnapshot.Value,
                isTabsCollapsed,
                canGoBack,
                canGoForward,
                () =>
                {
                    UpdateBrowserSession(state => BrowserSessionStore.SetTabsCollapsed(state, !isTabsCollapsed));
                },
                OpenCollectionsExpanded,
                isChatBladeOpen,
                ToggleChatBlade,
                ShowLinkerProviderKeyDialog,
                () => _browserWebViewHostController.GoBack(),
                () => _browserWebViewHostController.Reload(),
                () => _browserWebViewHostController.GoForward(),
                SubmitAddress,
                NavigateActiveTab,
                tabId =>
                {
                    var tabIndex = Array.FindIndex(tabs, tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal));
                    if (tabIndex >= 0)
                    {
                        SelectTab(tabIndex);
                    }
                },
                url => OpenUriInNewTab(url, dismissCommandCenter: false),
                selectedSearchProviderKey,
                BrowserSearchProviders.Providers,
                SetDefaultSearchProvider,
                SetCurrentPageAsHome,
                ToggleFavorite,
                async () =>
                {
                    var imageDataUrl = await _browserWebViewHostController.CaptureActivePageImageAsync();
                    await WindowsShareService.SharePageAsync(
                        selectedTab.Title,
                        selectedTab.Url,
                        imageDataUrl);
                },
                // pwa's & apps     
                selectedInstallableWebApp,
                isSelectedWebAppInstalled,
                InstallCurrentWebApp,
                OpenCurrentWebApp,
                SaveSettingValue,
                async (extensionId, enabled) =>
                {
                    var definition = BrowserExtensionService.Extensions.First(extension =>
                        string.Equals(extension.Id, extensionId, StringComparison.Ordinal));

                    try
                    {
                        await _browserWebViewHostController.SetExtensionEnabledAsync(extensionId, enabled);
                        SaveSettingValue(definition.SettingKey, enabled ? "true" : "false");
                        System.Diagnostics.Debug.WriteLine(
                            $"DEBUG: {definition.DisplayName} {(enabled ? "started" : "stopped")}");
                        _browserWebViewHostController.ReloadWithNotice(
                            string.Equals(definition.Id, "ublock-origin-lite", StringComparison.Ordinal)
                                ? $"Your ad blocker is now {(enabled ? "enabled" : "disabled")}."
                                : $"{definition.DisplayName} is now {(enabled ? "enabled" : "disabled")}.");
                    }
                    catch (Exception ex)
                    {
                        BrowserNoticeService.Show($"Could not update {definition.DisplayName}: {ex.Message}");
                    }
                },
                ClearBrowserCache,
                ClearBrowserCookies,
                ClearCoreBrowsingHistory,
                OpenSelectedTabInNewWindow,
                AddTab,
                CloseActiveTab));

        var tabRail = Component<BrowserTabRail, BrowserTabRailProps>(
            new BrowserTabRailProps(
                tabs,
                selectedIndex,
                selectedTag,
                isTabsCollapsed,
                isLoading,
                AddTab,
                () => UpdateBrowserSession(state =>
                    BrowserSessionStore.MaximizeRailTabs(
                        BrowserSessionStore.SetTabsCollapsed(state, false))),
                _browserTitleBarController.OpenCommandPalette,
                SelectTab,
                ToggleFavoriteTab,
                CloseTab,
                ReloadTab,
                OpenTabInNewWindow,
                MoveTab,
                GetTabInstalledWebAppName,
                GetTabInstallableWebAppName,
                OpenTabAsWebApp,
                InstallTabWebApp,
                activeCommandCenterSection,
                isCommandCenterExpanded,
                mostVisitedHistory.Value,
                recentHistory.Value,
                historyFilter.Value,
                historyLimit.Value,
                historyImportStatus.Value,
                historyImportBrowserProfiles.Value,
                favoriteItems.Value,
                favoritesLimit.Value,
                tabCollections.Value,
                collectionItems.Value,
                collectionMembership.Value,
                collectionName.Value,
                collectionStatus.Value,
                favoritesFilter.Value,
                favoritesImportStatus.Value,
                favoritesImportBrowserProfiles.Value,
                isCommandCenterBusy.Value,
                isCommandCenterHighlighted.Value,
                commandCenterBusyText.Value,
                settingsSnapshot.Value,
                SaveSettingValue,
                ApplyHistoryFilter,
                LoadMoreHistory,
                ApplyFavoritesFilter,
                LoadMoreFavorites,
                ApplyCollectionName,
                CreateCollection,
                CreateSmartCollections,
                nextCollectionName => RefreshCollectionState(nextCollectionName),
                DeleteSelectedCollection,
                AddCurrentTabToCollection,
                AddUrlToCollection,
                SetStartupCollection,
                ImportBrowserHistory,
                ImportBrowserHistoryByName,
                ImportBrowserHistoryByProfile,
                DeleteAllHistory,
                ImportBrowserFavorites,
                ImportBrowserFavoritesByName,
                ImportBrowserFavoritesByProfile,
                DeleteAllFavorites,
                OpenHistoryItem,
                OpenHistoryItemInNewTab,
                DeleteHistoryItem,
                OpenFavoriteItem,
                OpenFavoriteItemInNewTab,
                DeleteFavoriteItem,
                OpenCollectionItem,
                OpenCollectionItemInNewTab,
                RemoveCollectionItem,
                MoveCollectionItem,
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
                () =>
                {
                    _browserTitleBarController.SetAddressText(selectedTab.Url);

                    if (isCommandCenterOpen && isCommandCenterExpanded)
                    {
                        CompactCommandCenterForBrowsing();
                    }
                },
                UpdateTab,
                url => OpenUriInNewTab(url),
                SetTitleFromCore,
                SetNavAvailabilityIfNeeded,
                nextAddress => _browserTitleBarController.SetAddressText(
                    nextAddress,
                    preserveUserEdit: true),
                SetLoadingIfNeeded,
                RefreshVisibleHistoryAfterNavigation,
                SetInstallableWebAppFromCore,
                CloseTab
            ));
        var browserSurfaceInset = isFullScreenPresentationActive.Value
            ? 0
            : isTabsCollapsed
                ? BrowserSurfaceInsetCollapsed
                : BrowserSurfaceInsetExpanded;
        var browserSurfaceCornerRadius = isFullScreenPresentationActive.Value ? 0 : 12;

        var browserSurface = Border(
            Grid(
                [GridSize.Star()],
                [GridSize.Star()],
                (FlexRow(
                    Border(
                        Border(null)
                            .Width(1)
                        .Background(Theme.SurfaceStroke)
                        .Opacity(isTabsCollapsed ? 0.16 : 0.36)
                        .HAlign(HorizontalAlignment.Right)
                        .VAlign(VerticalAlignment.Stretch)
                    )
                    .Width(browserSurfaceInset)
                    .VAlign(VerticalAlignment.Stretch)
                    .CornerRadius(isFullScreenPresentationActive.Value ? 0 : 14)
                    .Flex(shrink: 0),
                    browserContent
                        .HAlign(HorizontalAlignment.Stretch)
                        .Flex(grow: 1, basis: 0)
                ).CornerRadius(browserSurfaceCornerRadius)
                with
                {
                    ColumnGap = 0
                })
                .Grid(row: 0, column: 0)
            )
            .CornerRadius(browserSurfaceCornerRadius)
        )
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch)
        .MinWidth(0)
        .CornerRadius(browserSurfaceCornerRadius)
        .Flex(grow: 1, basis: 0);

        var isLinkerCompact = isLinkerCompactState.Value;
        var fullLinkerOverlay = Border(
            (FlexColumn(
                (FlexRow(
                    TextBlock("Linker")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                        .VAlign(VerticalAlignment.Center)
                        .Flex(grow: 1, basis: 0),
                    Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphBackToWindow, 12), ToggleLinkerCompact)
                        .AutomationName("Compact Linker")
                        .ToolTip("Compact Linker")
                        .Width(34)
                        .Height(34)
                        .Padding(0)
                        .CornerRadius(17)
                        .Background(BrowserConstants.LayerFillDefaultBrush),
                    Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphHelp, 12), () => OpenUriInNewTab(BrowserConstants.LinkerHelpUrl, dismissCommandCenter: false))
                        .AutomationName("Open Linker help")
                        .ToolTip("Open Linker help")
                        .Width(34)
                        .Height(34)
                        .Padding(0)
                        .CornerRadius(17)
                        .Background(BrowserConstants.LayerFillDefaultBrush),
                    Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphClose, 12), CloseChatBlade)
                        .AutomationName("Close chat")
                        .Width(34)
                        .Height(34)
                        .Padding(0)
                        .CornerRadius(17)
                        .Background(BrowserConstants.LayerFillDefaultBrush)) with
                {
                    ColumnGap = 10
                }),
                Component<CommandCenterChatPanel, CommandCenterChatPanelProps>(
                    new CommandCenterChatPanelProps(
                        url => OpenUriInNewTab(url, dismissCommandCenter: false),
                        ShowLinkerProviderKeyDialog,
                        () => new CommandCenterChatContext(
                            selectedTab.Url,
                            selectedTab.Title,
                            ActiveTabId: selectedTab.Id,
                            CaptureActivePageImageAsync: _browserWebViewHostController.CaptureActivePageImageAsync),
                        ToggleLinkerCompact,
                        CloseChatBlade))
                    .Flex(grow: 1, basis: 0)) with
            {
                RowGap = 12
            }))
            .Width(520)
            .Padding(12)
            .Margin(12)
            .CornerRadius(18)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF2, 0x23, 0x23, 0x26)))
            .WithBorder(BrowserConstants.AccentFillColorDefaultBrush)
            .IsVisible(isChatBladeOpen && !isLinkerCompact)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Stretch)
            .Grid(row: 0, column: 0);

        var compactLinkerOverlay = Border(
            (FlexRow(
                TextBlock("Linker")
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                    .VAlign(VerticalAlignment.Center),
                TextBlock("Double-click to expand")
                    .Opacity(0.7)
                    .VAlign(VerticalAlignment.Center)
                    .Flex(grow: 1, basis: 0),
                Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphFullScreen, 12), ToggleLinkerCompact)
                    .AutomationName("Expand Linker")
                    .ToolTip("Expand Linker")
                    .Width(32)
                    .Height(32)
                    .Padding(0)
                    .CornerRadius(16)
                    .Background(BrowserConstants.LayerFillDefaultBrush),
                Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphClose, 11), CloseChatBlade)
                    .AutomationName("Close Linker")
                    .ToolTip("Close Linker")
                    .Width(32)
                    .Height(32)
                    .Padding(0)
                    .CornerRadius(16)
                    .Background(BrowserConstants.LayerFillDefaultBrush)) with
            {
                ColumnGap = 10
            }))
            .Width(380)
            .Height(56)
            .Padding(12, 8)
            .Margin(12, 12, 12, 16)
            .CornerRadius(18)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF2, 0x23, 0x23, 0x26)))
            .WithBorder(BrowserConstants.AccentFillColorDefaultBrush)
            .IsVisible(isChatBladeOpen && isLinkerCompact)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Bottom)
            .Set(border =>
            {
                border.DoubleTapped -= OnCompactLinkerDoubleTapped;
                border.DoubleTapped += OnCompactLinkerDoubleTapped;
                border.Tag = (Action)ToggleLinkerCompact;
            })
            .Grid(row: 0, column: 0);

        Element? firstRunSetup = isFirstRunSetupVisible.Value
            ? Component<FirstRunSetupPanel, FirstRunSetupPanelProps>(
                new FirstRunSetupPanelProps(
                    selectedSearchProviderKey,
                    BrowserSearchProviders.Providers,
                    BuildFirstRunBrowserOptions(
                        historyImportBrowserProfiles.Value,
                        favoritesImportBrowserProfiles.Value),
                    SetDefaultSearchProvider,
                    ImportFirstRunDataAsync,
                    CompleteFirstRunSetup))
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch)
                .Grid(row: 0, column: 0)
            : null;

        var firstRunImportStatus = BuildFirstRunImportStatus(
                firstRunImportNotice.Value,
                isFirstRunImportNoticeVisible.Value,
                () => isFirstRunImportNoticeVisible.Set(false))
            .Grid(row: 0, column: 0);

        var mainContent = Grid(
            [GridSize.Star()],
            [GridSize.Star()],
            FlexRow(
                tabRail.IsVisible(!isFullScreenPresentationActive.Value),
                browserSurface
            )
            .Backdrop(BackdropKind.Transparent)
            .Grid(row: 0, column: 0),
            fullLinkerOverlay,
            compactLinkerOverlay,
            firstRunImportStatus)
            .Flex(grow: 1, basis: 0);

        var browserLayout = FlexColumn(
                titleBar.IsVisible(!isFullScreenPresentationActive.Value),
                BuildBrowserNoticeBanner(browserNotice.Value),
                mainContent)
            .IsEnabled(!isFirstRunSetupVisible.Value)
            .Grid(row: 0, column: 0);

        return Grid(
            [GridSize.Star()],
            [GridSize.Star()],
            browserLayout,
            firstRunSetup);
    }

    private static Element BuildFirstRunImportStatus(
        FirstRunImportNotice? notice,
        bool isVisible,
        Action onDismiss)
    {
        if (!isVisible || notice is null)
        {
            return Border(null).IsVisible(false);
        }

        Element statusVisual = notice.IsBusy
            ? ProgressRing()
                .Width(20)
                .Height(20)
                .IsActive(true)
                .Set(ring => ring.Foreground = BrowserConstants.AccentFillColorDefaultBrush)
            : Border(
                BrowserIcons.FluentIcon(
                        notice.HasErrors ? BrowserConstants.GlyphWarning : BrowserConstants.GlyphCheckMark,
                        13)
                    .Foreground(new SolidColorBrush(Microsoft.UI.Colors.White)))
                .Width(24)
                .Height(24)
                .CornerRadius(12)
                .Background(notice.HasErrors
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xB8, 0x5C, 0x2C))
                    : BrowserConstants.AccentFillColorDefaultBrush);

        return Border(
            (FlexRow(
                statusVisual,
                VStack(2,
                    TextBlock(notice.IsBusy
                            ? "Importing browser data"
                            : notice.HasErrors
                                ? "Import finished with issues"
                                : "Import complete")
                        .Set(text => text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBlock(notice.Message)
                        .Opacity(0.76)
                        .TextWrapping(TextWrapping.WrapWholeWords))
                    .Flex(grow: 1, basis: 0),
                Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphClose, 11), onDismiss)
                    .AutomationName("Dismiss browser import status")
                    .ToolTip("Dismiss")
                    .Width(28)
                    .Height(28)
                    .Padding(0)
                    .CornerRadius(14)
                    .Background(BrowserConstants.SubtleFillColorSecondaryBrush)) with
            {
                ColumnGap = 12
            }))
            .Width(320)
            .Height(72)
            .Padding(12, 10)
            .Margin(12)
            .CornerRadius(8)
            .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
            .WithBorder(Theme.SurfaceStroke)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Top);
    }

    private static void OnCompactLinkerDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: Action toggleCompact })
        {
            args.Handled = true;
            toggleCompact();
        }
    }

    private void RegisterFullScreenPresentationMessenger(Action<bool> setIsFullScreenPresentationActive)
    {
        _setFullScreenPresentationState = setIsFullScreenPresentationActive;

        if (_fullScreenPresentationMessengerRegistered)
        {
            return;
        }

        _fullScreenPresentationMessengerRegistered = true;
        Messenger.Register<TabViewPage, WebViewFullScreenPresentationChangedMessage>(
            this,
            static (recipient, message) =>
                recipient._setFullScreenPresentationState?.Invoke(message.IsFullScreen));
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
                BrowserIcons.FluentIcon(
                    string.Equals(browserNotice.Severity, "info", StringComparison.OrdinalIgnoreCase)
                        ? BrowserConstants.GlyphInfo
                        : BrowserConstants.GlyphWarning,
                    14),
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
        if (TryLoadStartupCollectionTabs(out var collectionTabs))
        {
            return collectionTabs;
        }

        if (!IsSaveTabsEnabled())
        {
            return [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
        }

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

    private static BrowserTab[] LoadSavedTabs()
    {
        try
        {
            var persisted = TabPersistenceService.LoadTabs<BrowserTab[]>("tabs");
            var safeTabs = persisted is null ? [] : SanitizeTabs(persisted);
            return safeTabs.Length > 0
                ? ReconcileTabsWithPersistedFavorites(safeTabs)
                : [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
        }
        catch
        {
            return [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
        }
    }

    private static BrowserTab ResolveStartupSelectedTab(BrowserTab[] startupTabs)
    {
        if (startupTabs.Length == 0)
        {
            return BrowserTab.CreateHome(GetConfiguredHomeUrl());
        }

        try
        {
            var persistedSelectedTabId = TabPersistenceService.LoadTabs<string>("selectedTabId");
            var selectedById = startupTabs.FirstOrDefault(tab => tab.Id == persistedSelectedTabId);
            if (selectedById is not null)
            {
                return selectedById;
            }

            var persistedSelectedTabUrl = TabPersistenceService.LoadTabs<string>("selectedTabUrl");
            var selectedByPersistedUrl = startupTabs.FirstOrDefault(tab =>
                BrowserUrl.AreEqual(tab.Url, persistedSelectedTabUrl));
            if (selectedByPersistedUrl is not null)
            {
                return selectedByPersistedUrl;
            }

            var persistedTabs = TabPersistenceService.LoadTabs<BrowserTab[]>("tabs");
            var persistedSelectedTab = persistedTabs?.FirstOrDefault(tab => tab.Id == persistedSelectedTabId);
            if (persistedSelectedTab is not null)
            {
                var selectedByUrl = startupTabs.FirstOrDefault(tab =>
                    BrowserUrl.AreEqual(tab.Url, persistedSelectedTab.Url));
                if (selectedByUrl is not null)
                {
                    return selectedByUrl;
                }
            }
        }
        catch
        {
        }

        return startupTabs[0];
    }

    private static bool TryLoadStartupCollectionTabs(out BrowserTab[] tabs)
    {
        try
        {
            var startupCollection = TabCollectionService.GetStartupCollection();
            if (startupCollection is null)
            {
                tabs = [];
                return false;
            }

            tabs = LoadCollectionTabs(startupCollection.Id);
            return true;
        }
        catch
        {
            tabs = [];
            return false;
        }
    }

    private static BrowserTab[] SetAndLoadStartupCollection(string collectionId)
    {
        TabCollectionService.SetStartupCollection(collectionId);
        return LoadCollectionTabs(collectionId);
    }

    private static BrowserTab[] LoadCollectionTabs(string collectionId)
    {
        var tabs = TabCollectionService.GetItems(collectionId)
            .Take(MaxTabs)
            .Select((item, index) =>
                BrowserTab.CreateNew(index + 1, item.Url, visitCount: 0) with
                {
                    Title = item.Title,
                    Order = index
                })
            .ToArray();

        return tabs.Length > 0 ? tabs : [BrowserTab.CreateHome(GetConfiguredHomeUrl())];
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

    private static BrowserTab[] CreateFreshWindowTabs(
        string activationTarget,
        string selectedSearchProviderKey,
        out BrowserTab activatedTab)
    {
        var fallback = GetConfiguredHomeUrl();
        var normalizedTarget = BrowserUrl.Normalize(activationTarget, fallback, selectedSearchProviderKey);
        activatedTab = BrowserTab.CreateNew(1, normalizedTarget, visitCount: 1);
        return [activatedTab];
    }

    private static BrowserTab[] CreateTabsFromPackage(
        ActiveTabsPackage package,
        string selectedSearchProviderKey,
        out string selectedTabId)
    {
        var fallback = GetConfiguredHomeUrl();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var items = package.ValidTabs
            .Select((tab, index) => new { Tab = tab, InputIndex = index })
            .OrderBy(item => item.Tab.Order ?? item.InputIndex)
            .Take(MaxTabs)
            .ToArray();

        var tabs = items
            .Select((item, index) =>
            {
                var source = item.Tab;
                var normalizedUrl = BrowserUrl.Normalize(source.Url, fallback, selectedSearchProviderKey);
                var id = !string.IsNullOrWhiteSpace(source.Id) && usedIds.Add(source.Id.Trim())
                    ? source.Id.Trim()
                    : Guid.NewGuid().ToString("N");
                var title = string.IsNullOrWhiteSpace(source.Title)
                    ? normalizedUrl
                    : Trim(source.Title, MaxTitleLength);
                var favoriteId = source.IsFavorite == true
                    ? string.IsNullOrWhiteSpace(source.FavoriteId) ? Guid.NewGuid().ToString("N") : source.FavoriteId.Trim()
                    : string.Empty;

                return new BrowserTab(
                    id,
                    title,
                    Trim(normalizedUrl, MaxUrlLength),
                    DateTime.Now,
                    favoriteId,
                    Math.Max(0, source.VisitedCount ?? 0),
                    source.IsFavorite == true,
                    source.IsHomeTab == true,
                    index,
                    Math.Max(0, source.ScrollX ?? 0),
                    Math.Max(0, source.ScrollY ?? 0),
                    source.IsSleeping == true);
            })
            .ToArray();

        if (tabs.Length == 0)
        {
            var homeTab = BrowserTab.CreateHome(fallback);
            selectedTabId = homeTab.Id;
            return [homeTab];
        }

        selectedTabId =
            ResolvePackageSelectedTabId(package, items.Select(item => item.Tab).ToArray(), tabs)
            ?? tabs[0].Id;
        return tabs;

        static string Trim(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? ResolvePackageSelectedTabId(
        ActiveTabsPackage package,
        ActiveTabItem[] sourceTabs,
        BrowserTab[] tabs)
    {
        if (!string.IsNullOrWhiteSpace(package.SelectedTabId))
        {
            var selectedById = tabs.FirstOrDefault(tab => string.Equals(tab.Id, package.SelectedTabId, StringComparison.Ordinal));
            if (selectedById is not null)
            {
                return selectedById.Id;
            }
        }

        var selectedSourceIndex = Array.FindIndex(sourceTabs, tab => tab.Selected == true);
        if (selectedSourceIndex >= 0 && selectedSourceIndex < tabs.Length)
        {
            return tabs[selectedSourceIndex].Id;
        }

        if (package.SelectedIndex is int selectedIndex &&
            selectedIndex >= 0 &&
            selectedIndex < tabs.Length)
        {
            return tabs[selectedIndex].Id;
        }

        return null;
    }

    private static BrowserTab[] AppendImportedTabs(BrowserTab[] currentTabs, BrowserTab[] importedTabs)
    {
        var existingIds = currentTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var appendedTabs = importedTabs
            .Select((tab, index) => existingIds.Add(tab.Id)
                ? tab
                : tab with { Id = Guid.NewGuid().ToString("N") })
            .ToArray();

        BrowserTab[] combinedTabs = [.. currentTabs, .. appendedTabs];

        return combinedTabs
            .Take(MaxTabs)
            .Select((tab, index) => tab with { Order = index })
            .ToArray();
    }

    private static void SavePackageCollection(ActiveTabsPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.CollectionName))
        {
            return;
        }

        foreach (var tab in package.ValidTabs)
        {
            try
            {
                TabCollectionService.AddOrUpdateItem(package.CollectionName, tab.Url, tab.Title);
            }
            catch
            {
            }
        }
    }

    private static IReadOnlyDictionary<string, string[]> BuildCollectionMembership(IReadOnlyList<TabCollection> collections)
    {
        var membership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in collections)
        {
            foreach (var item in TabCollectionService.GetItems(collection.Id))
            {
                if (!membership.TryGetValue(item.Url, out var collectionNames))
                {
                    collectionNames = [];
                    membership[item.Url] = collectionNames;
                }

                if (!collectionNames.Contains(collection.Name, StringComparer.OrdinalIgnoreCase))
                {
                    collectionNames.Add(collection.Name);
                }
            }
        }

        return membership.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
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

    private static IReadOnlyDictionary<string, BrowserImportProfile[]> GetHistoryImportBrowserProfiles()
    {
        try
        {
            return BrowserHistoryImportService.DiscoverSources()
                .GroupBy(source => source.BrowserName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(source => new BrowserImportProfile(source.ProfileName, source.ProfileLabel))
                        .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, BrowserImportProfile[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyDictionary<string, BrowserImportProfile[]> GetFavoritesImportBrowserProfiles()
    {
        try
        {
            return BrowserFavoritesImportService.DiscoverSources()
                .GroupBy(source => source.BrowserName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(source => new BrowserImportProfile(source.ProfileName, source.ProfileLabel))
                        .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, BrowserImportProfile[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static FirstRunBrowserOption[] BuildFirstRunBrowserOptions(
        IReadOnlyDictionary<string, BrowserImportProfile[]> historyProfiles,
        IReadOnlyDictionary<string, BrowserImportProfile[]> favoriteProfiles)
    {
        return historyProfiles.Keys
            .Union(favoriteProfiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(browserName =>
            {
                historyProfiles.TryGetValue(browserName, out var browserHistoryProfiles);
                favoriteProfiles.TryGetValue(browserName, out var browserFavoriteProfiles);

                var profiles = (browserHistoryProfiles ?? [])
                    .Concat(browserFavoriteProfiles ?? [])
                    .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        var profile = group.First();
                        return new FirstRunProfileOption(profile.Id, profile.Name);
                    })
                    .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new FirstRunBrowserOption(browserName, profiles);
            })
            .ToArray();
    }

    private static string GetProfileLabel(
        IReadOnlyDictionary<string, BrowserImportProfile[]> browserProfiles,
        string browserName,
        string profileName)
    {
        if (browserProfiles.TryGetValue(browserName, out var profiles))
        {
            var match = profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileName, StringComparison.OrdinalIgnoreCase));

            if (match is not null && !string.IsNullOrWhiteSpace(match.Name))
            {
                return match.Name;
            }
        }

        return profileName;
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

    private static FavoriteItem[] LoadFavoriteItems(string? filter, int limit)
    {
        try
        {
            return string.IsNullOrWhiteSpace(filter)
                ? FavoritesService.GetFavorites(limit).ToArray()
                : FavoritesService.SearchFavorites(filter, limit).ToArray();
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

    private static async Task<bool> ConfirmDestructiveActionAsync(string title, string message, string primaryButtonText)
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

    private static async Task ShowLinkerProviderResultDialogAsync(string title, string message)
    {
        var xamlRoot = global::LinkScape.Application.MainWindowActivation.GetXamlRoot();
        if (xamlRoot is null)
        {
            BrowserNoticeService.Show(message);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
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
                if (target.Kind != ActivationTargetKind.InstalledApp)
                {
                    global::LinkScape.Application.MainWindowActivation.RestoreAndActivate();
                }

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
        if (_suppressTabPersistence)
        {
            return;
        }

        if (!IsSaveTabsEnabled())
        {
            ClearPersistedStartupTabs();
            return;
        }

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
            SaveSelectedTabUrl(tabs, selectedTabId);
        }
        catch
        {
        }
    }

    private void TrackSelectedTab(string? selectedTabId, BrowserTab[] tabs)
    {
        var openTabIds = tabs
            .Select(tab => tab.Id)
            .ToHashSet(StringComparer.Ordinal);

        _tabActivationHistory.RemoveAll(tabId => !openTabIds.Contains(tabId));

        if (string.IsNullOrWhiteSpace(selectedTabId) ||
            !openTabIds.Contains(selectedTabId) ||
            string.Equals(_lastTrackedSelectedTabId, selectedTabId, StringComparison.Ordinal))
        {
            return;
        }

        _tabActivationHistory.RemoveAll(tabId => string.Equals(tabId, selectedTabId, StringComparison.Ordinal));
        _tabActivationHistory.Insert(0, selectedTabId);
        _lastTrackedSelectedTabId = selectedTabId;
    }

    private string? GetLastActiveOpenTabId(string closingTabId, BrowserTab[] tabs)
    {
        var openTabIds = tabs
            .Where(tab => !string.Equals(tab.Id, closingTabId, StringComparison.Ordinal))
            .Select(tab => tab.Id)
            .ToHashSet(StringComparer.Ordinal);

        return _tabActivationHistory.FirstOrDefault(tabId =>
            !string.Equals(tabId, closingTabId, StringComparison.Ordinal) &&
            openTabIds.Contains(tabId));
    }

    private void ForgetClosedTab(string tabId)
    {
        _tabActivationHistory.RemoveAll(candidate =>
            string.Equals(candidate, tabId, StringComparison.Ordinal));

        if (string.Equals(_lastTrackedSelectedTabId, tabId, StringComparison.Ordinal))
        {
            _lastTrackedSelectedTabId = null;
        }
    }

    private void SuppressTabPersistence()
    {
        _suppressTabPersistence = true;
        _saveTabsCts?.Cancel();
        _saveTabsCts?.Dispose();
        _saveTabsCts = null;
    }

    private void ScheduleTabsSave(BrowserTab[] tabs, string selectedTabId)
    {
        if (_suppressTabPersistence)
        {
            _latestTabs = tabs;
            _latestSelectedTabId = selectedTabId;
            return;
        }

        if (!IsSaveTabsEnabled())
        {
            _saveTabsCts?.Cancel();
            _saveTabsCts?.Dispose();
            _saveTabsCts = null;
            ClearPersistedStartupTabs();
            return;
        }

        _latestTabs = tabs;
        _latestSelectedTabId = selectedTabId;

        _saveTabsCts?.Cancel();
        _saveTabsCts?.Dispose();

        var snapshotTabs = tabs.ToArray();
        var snapshotSelectedTabId = selectedTabId;
        var snapshotSelectedTabUrl = tabs.FirstOrDefault(tab => tab.Id == selectedTabId)?.Url;
        var cts = new CancellationTokenSource();

        _saveTabsCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, cts.Token);

                TabPersistenceService.SaveTabs("tabs", snapshotTabs);
                TabPersistenceService.SaveTabs("selectedTabId", snapshotSelectedTabId);
                if (!string.IsNullOrWhiteSpace(snapshotSelectedTabUrl))
                {
                    TabPersistenceService.SaveTabs("selectedTabUrl", snapshotSelectedTabUrl);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    private static void SaveSelectedTabUrl(BrowserTab[] tabs, string selectedTabId)
    {
        var selectedTabUrl = tabs.FirstOrDefault(tab => tab.Id == selectedTabId)?.Url;
        if (!string.IsNullOrWhiteSpace(selectedTabUrl))
        {
            TabPersistenceService.SaveTabs("selectedTabUrl", selectedTabUrl);
        }
    }

    private static bool IsSaveTabsEnabled(IReadOnlyDictionary<string, string>? settingsSnapshot = null)
    {
        var configuredValue = settingsSnapshot is not null &&
            settingsSnapshot.TryGetValue(SaveTabsSettingKey, out var snapshotValue)
                ? snapshotValue
                : SettingsService.GetValueOrDefault(SaveTabsSettingKey, "true");

        return !bool.TryParse(configuredValue, out var isEnabled) || isEnabled;
    }

    private static void ClearPersistedStartupTabs()
    {
        try
        {
            TabPersistenceService.RemoveTabs("tabs");
            TabPersistenceService.RemoveTabs("selectedTabId");
            TabPersistenceService.RemoveTabs("selectedTabUrl");
        }
        catch
        {
        }
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
                ScrollY = Math.Max(0, tab.ScrollY),
                IsSleeping = false
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
