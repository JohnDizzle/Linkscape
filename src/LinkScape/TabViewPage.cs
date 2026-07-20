using LinkScape.Browser;
using LinkScape.Browser.State;
using LinkScape.Models;
using Browser.Components;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape;

class TabViewPage : Component
{
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";
    private const string HomeUrlSettingKey = BrowserConstants.HomeUrlSettingKey;
    private const string SaveTabsSettingKey = BrowserConstants.SaveTabsSettingKey;
    private const double BrowserSurfaceInsetCollapsed = 2;
    private const double BrowserSurfaceInsetExpanded = 4;
    private const int CommandCenterBusyMinimumDurationMilliseconds = 220;

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
    private DateTime _commandCenterBusyStartedAtUtc;
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

        var session = UseState(_latestBrowserSession);

        _latestBrowserSession = session.Value;
        var tabs = session.Value.Tabs;
        _latestTabs = tabs;
        var selectedTag = session.Value.SelectedTabId;
        _latestSelectedTabId = selectedTag;
        var isTabsCollapsed = session.Value.IsTabsCollapsed;
        var canGoBack = session.Value.CanGoBack;
        var canGoForward = session.Value.CanGoForward;
        var isLoading = session.Value.IsLoading;
        var historyFilter = UseState(string.Empty);
        var historyLimit = UseState(50);
        var recentHistory = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var mostVisitedHistory = UseState(Array.Empty<HistoryItem>(), threadSafe: true);
        var favoritesFilter = UseState(string.Empty);
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
        var browserNotice = UseState<BrowserNotice?>(BrowserNoticeService.CurrentNotice, threadSafe: true);
        var selectedSearchProviderKey = session.Value.SelectedSearchProviderKey;
        var isCommandCenterOpen = session.Value.IsCommandCenterOpen;
        var isChatBladeOpen = session.Value.IsChatOpen;
        var configuredHomeUrl = GetConfiguredHomeUrl(settingsSnapshot.Value);

        RegisterBrowserNoticeListener(browserNotice.Set);

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

        void DismissCommandCenter()
        {
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
            var xamlRoot = global::MainWindowActivation.GetXamlRoot();
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
            recentHistory.Set(LoadRecentHistoryItems(nextFilter, 50));
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
            favoriteItems.Set(LoadFavoriteItems(effectiveFilter));
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
            favoriteItems.Set(LoadFavoriteItems(nextFilter));
        }

        void SetCollectionStateFromDatabase(string? collectionNameOverride = null)
        {
            var collections = TabCollectionService.GetCollections().ToArray();
            var effectiveName = collectionNameOverride ?? collectionName.Value;

            if (string.IsNullOrWhiteSpace(effectiveName))
            {
                effectiveName = collections.FirstOrDefault()?.Name ?? "Personal";
            }

            tabCollections.Set(collections);
            collectionName.Set(effectiveName);
            collectionItems.Set(TabCollectionService.GetItems(effectiveName).ToArray());
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
                collectionStatus.Set($"Collection '{collection.Name}' is ready.");
                RefreshCollectionState(collection.Name);
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

        async void DeleteAllHistory()
        {
            var confirmed = await ConfirmDestructiveActionAsync(
                "Delete all history?",
                "This permanently removes all saved browsing history from LinkScape.",
                "Delete history");

            if (!confirmed)
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
                TabCollectionService.RemoveItem(collectionName.Value, url);
                collectionStatus.Set("Removed item from collection.");
                RefreshCollectionState(collectionName.Value);
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
                selectedSearchProviderKey,
                BrowserSearchProviders.Providers,
                SetDefaultSearchProvider,
                SetCurrentPageAsHome,
                ToggleFavorite,
                SaveSettingValue,
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
                mostVisitedHistory.Value,
                recentHistory.Value,
                historyFilter.Value,
                historyLimit.Value,
                historyImportStatus.Value,
                historyImportBrowserProfiles.Value,
                favoriteItems.Value,
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
                ApplyCollectionName,
                CreateCollection,
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
                nextAddress => _browserTitleBarController.SetAddressText(nextAddress, preserveUserEdit: true),
                SetLoadingIfNeeded,
                () => RefreshHistoryState()));

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
                    .Width(isTabsCollapsed ? BrowserSurfaceInsetCollapsed : BrowserSurfaceInsetExpanded)
                    .VAlign(VerticalAlignment.Stretch).CornerRadius(14)
                    .Flex(shrink: 0),
                    browserContent
                        .HAlign(HorizontalAlignment.Stretch)
                        .Flex(grow: 1, basis: 0)
                ).CornerRadius(12)
                with
                {
                    ColumnGap = 0
                })
                .Grid(row: 0, column: 0)
            )
            .CornerRadius(12)
        )
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch)
        .MinWidth(0)
        .CornerRadius(12)
        .Flex(grow: 1, basis: 0);

        var chatOverlay = Border(
            FlexColumn(
                FlexRow(
                    TextBlock("Linker")
                        .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                        .VAlign(VerticalAlignment.Center)
                        .Flex(grow: 1, basis: 0),
                    Button(BrowserIcons.FluentIcon(BrowserConstants.GlyphClose, 12), CloseChatBlade)
                        .AutomationName("Close chat")
                        .Width(34)
                        .Height(34)
                        .Padding(0)
                        .CornerRadius(17)
                        .Background(BrowserConstants.LayerFillDefaultBrush)) with
                {
                    ColumnGap = 10
                },
                Component<CommandCenterChatPanel, CommandCenterChatPanelProps>(
                    new CommandCenterChatPanelProps(
                        url => OpenUriInNewTab(url, dismissCommandCenter: false),
                        ShowLinkerProviderKeyDialog,
                        () => new CommandCenterChatContext(selectedTab.Url, selectedTab.Title)))
                    .Flex(grow: 1, basis: 0)) with
            {
                RowGap = 12
            })
            .Width(520)
            .Padding(12)
            .Margin(12)
            .CornerRadius(18)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF2, 0x23, 0x23, 0x26)))
            .WithBorder(BrowserConstants.AccentFillColorDefaultBrush)
            .IsVisible(isChatBladeOpen)
            .HAlign(HorizontalAlignment.Right)
            .VAlign(VerticalAlignment.Stretch)
            .Grid(row: 0, column: 0);

        var mainContent = Grid(
            [GridSize.Star()],
            [GridSize.Star()],
            FlexRow(
                tabRail,
                browserSurface
            )
            .Backdrop(BackdropKind.Transparent)
            .Grid(row: 0, column: 0),
            chatOverlay)
            .Flex(grow: 1, basis: 0);

        return FlexColumn(
            titleBar,
            BuildBrowserNoticeBanner(browserNotice.Value),
            mainContent
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
        var collectionTabs = LoadStartupCollectionTabs();
        if (collectionTabs.Length > 0)
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

    private static BrowserTab[] LoadStartupCollectionTabs()
    {
        try
        {
            var startupCollection = TabCollectionService.GetStartupCollection();
            if (startupCollection is null)
            {
                return [];
            }

            var items = TabCollectionService.GetItems(startupCollection.Id);
            return items
                .Take(MaxTabs)
                .Select((item, index) =>
                    BrowserTab.CreateNew(index + 1, item.Url, visitCount: 0) with
                    {
                        Title = item.Title,
                        Order = index
                    })
                .ToArray();
        }
        catch
        {
            return [];
        }
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

    private static async Task<bool> ConfirmDestructiveActionAsync(string title, string message, string primaryButtonText)
    {
        var xamlRoot = global::MainWindowActivation.GetXamlRoot();
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
        var xamlRoot = global::MainWindowActivation.GetXamlRoot();
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
                global::MainWindowActivation.RestoreAndActivate();
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
        }
        catch
        {
        }
    }

    private void ScheduleTabsSave(BrowserTab[] tabs, string selectedTabId)
    {
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
