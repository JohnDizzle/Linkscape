using LinkScape.Browser;
using LinkScape.Models;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape.Browser.Components;

internal sealed class BrowserTitleBarController
{
    internal Action<string, bool>? SetAddressTextCore { get; set; }
    internal Action? OpenCommandPaletteCore { get; set; }
    internal Action? RefreshCommandPaletteCore { get; set; }

    public void SetAddressText(string value, bool preserveUserEdit = false) =>
        SetAddressTextCore?.Invoke(value, preserveUserEdit);

    public void OpenCommandPalette() => OpenCommandPaletteCore?.Invoke();

    public void RefreshCommandPalette() => RefreshCommandPaletteCore?.Invoke();
}

internal sealed record BrowserTitleBarProps(
    BrowserTitleBarController Controller,
    BrowserWebViewHostController BrowserController,
    BrowserTab SelectedTab,
    IReadOnlyList<BrowserTab> Tabs,
    string HomeUrl,
    IReadOnlyDictionary<string, string> SettingsSnapshot,
    bool IsTabsCollapsed,
    bool CanGoBack,
    bool CanGoForward,
    Action OnToggleTabs,
    Action OnOpenCollections,
    bool IsChatOpen,
    Action OnToggleChat,
    Action OnOpenAiKeyDialog,
    Action OnBack,
    Action OnRefresh,
    Action OnForward,
    Action<string> OnSubmitAddress,
    Action<string> OnNavigateCurrentTab,
    Action<string> OnActivateTab,
    Action<string> OnOpenAddressInNewTab,
    string SelectedSearchProviderKey,
    IReadOnlyList<BrowserSearchProvider> SearchProviders,
    Action<string> OnSelectSearchProvider,
    Action OnSetCurrentPageAsHome,
    Action OnToggleFavorite,
    Action OnShareCurrentPage,
    // Pwa and apps. 
    InstallableWebApp? InstallableWebApp,
    bool IsWebAppInstalled,
    Action OnInstallWebApp,
    Action OnOpenWebApp,
    Action<string, string> OnSaveSettingValue,
    Action<string, bool> OnToggleExtension,
    Action OnClearCache,
    Action OnClearCookies,
    Action OnClearBrowsingHistory,
    Action OnOpenSelectedTabInNewWindow,
    Action OnAddTab,
    Action OnCloseTab);

internal sealed class BrowserTitleBar : Component<BrowserTitleBarProps>
{
    private const int PalettePageSize = 10;
    private const int PaletteMaximumItems = 100;
    private Microsoft.UI.Xaml.Controls.AutoSuggestBox? _addressBox;
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _searchPopup;
    private CommandPaletteControl? _commandPaletteControl;
    private CancellationTokenSource? _searchCancellation;
    private AddressSearchSource _selectedSearchSource = AddressSearchSource.All;
    private IReadOnlyList<AddressSearchResult> _searchResults = [];
    private IReadOnlyList<AddressSearchCollectionGroup> _collectionGroups = [];
    private string? _expandedCollectionId;
    private string _collectionFilterQuery = string.Empty;
    private bool _isWebSearchRunning;
    private int _localSearchLimit = PalettePageSize;
    private bool _hasMoreLocalResults;
    private string _searchError = string.Empty;
    private string _addressBarText = string.Empty;
    private string _paletteFilterText = string.Empty;
    private bool _isAddressBarEditing;
    private bool _suppressAddressBoxTextChanged;
    private bool _isCommandPaletteRequested;
    private bool _isInitialized;

    public override Element Render()
    {
        var useCompactLayout = UseState(false);

        if (!_isInitialized)
        {
            _addressBarText = Props.SelectedTab.Url;
            _isInitialized = true;
        }

        Props.Controller.SetAddressTextCore = SetAddressBarText;
        Props.Controller.OpenCommandPaletteCore = OpenCommandPalette;
        Props.Controller.RefreshCommandPaletteCore = RefreshCommandPalette;

        return BrowserChrome.BuildTitleBar(
            Props.SelectedTab,
            Props.BrowserController,
            useCompactLayout.Value,
            width =>
            {
                var nextCompactLayout = BrowserChrome.UseCompactTitleBar(width);
                if (nextCompactLayout != useCompactLayout.Value)
                {
                    useCompactLayout.Set(nextCompactLayout);
                }
            },
            _addressBarText,
            Props.HomeUrl,
            Props.SettingsSnapshot,
            Props.IsTabsCollapsed,
            Props.CanGoBack,
            Props.CanGoForward,
            Props.OnToggleTabs,
            Props.OnOpenCollections,
            Props.IsChatOpen,
            Props.OnToggleChat,
            Props.OnOpenAiKeyDialog,
            Props.OnBack,
            Props.OnRefresh,
            Props.OnForward,
            SetAddressBarDraft,
            SubmitAddressAndCloseSearch,
            AttachAddressBox,
            Props.OnNavigateCurrentTab,
            Props.OnOpenAddressInNewTab,
            Props.SelectedSearchProviderKey,
            Props.SearchProviders,
            Props.OnSelectSearchProvider,
            Props.OnSetCurrentPageAsHome,
            Props.OnToggleFavorite,
            Props.OnShareCurrentPage,
            Props.InstallableWebApp,
            Props.IsWebAppInstalled,
            Props.OnInstallWebApp,
            Props.OnOpenWebApp,
            Props.OnSaveSettingValue,
            Props.OnToggleExtension,
            Props.OnClearCache,
            Props.OnClearCookies,
            Props.OnClearBrowsingHistory,
            Props.OnOpenSelectedTabInNewWindow,
            Props.OnAddTab,
            Props.OnCloseTab);
    }

    private void SubmitAddressAndCloseSearch(string value)
    {
        CloseSearchPopup();
        Props.OnSubmitAddress(value);
    }

    private void AttachAddressBox(Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
    {
        if (!ReferenceEquals(_addressBox, addressBox))
        {
            if (_addressBox is not null)
            {
                _addressBox.KeyDown -= OnAddressBoxKeyDown;
                _addressBox.LostFocus -= OnAddressBoxLostFocus;
            }

            addressBox.KeyDown += OnAddressBoxKeyDown;
            addressBox.LostFocus += OnAddressBoxLostFocus;
        }

        _addressBox = addressBox;

        if (!string.Equals(addressBox.Text, _addressBarText, StringComparison.Ordinal))
        {
            _suppressAddressBoxTextChanged = true;
            addressBox.Text = _addressBarText;
            _suppressAddressBoxTextChanged = false;
        }

        UpdateCommandPaletteNotification();
    }

    private void SetAddressBarDraft(string value)
    {
        if (_suppressAddressBoxTextChanged)
        {
            return;
        }

        _selectedSearchSource = AddressSearchSource.All;
        _isAddressBarEditing = true;
        _addressBarText = value;
        if (IsAddressBoxFocused())
        {
            ScheduleLocalSearch(value);
        }
    }

    private void OpenCommandPalette()
    {
        var addressBox = _addressBox;
        if (addressBox?.XamlRoot is null)
        {
            return;
        }

        if (_isCommandPaletteRequested && _searchPopup?.IsOpen == true)
        {
            _commandPaletteControl?.FocusFilter();
            return;
        }

        _selectedSearchSource = AddressSearchSource.All;
        _searchResults = [];
        _searchError = string.Empty;
        _isCommandPaletteRequested = true;
        _isAddressBarEditing = true;
        _paletteFilterText = string.Empty;
        _commandPaletteControl = null;
        UpdateCommandPaletteNotification();
        RenderSearchPopup(string.Empty);
        ScheduleLocalSearch(string.Empty);
    }

    private string GetCommandPalettePlaceholder() => _selectedSearchSource switch
    {
        AddressSearchSource.Tabs => "Filter active tabs",
        AddressSearchSource.History => "Filter history",
        AddressSearchSource.Favorites => "Filter favorites",
        AddressSearchSource.Collections => "Filter collections",
        _ => "Search tabs, history, favorites, and collections"
    };

    private void UpdateCommandPaletteNotification()
    {
        if (_addressBox is null)
        {
            return;
        }

        _addressBox.PlaceholderText = "Search or enter web address";
        _addressBox.QueryIcon = new FontIcon
        {
            FontFamily = BrowserConstants.IconFontFamily,
            Glyph = _isCommandPaletteRequested
                ? GetSearchSourceGlyph(_selectedSearchSource)
                : BrowserConstants.GlyphMagnifyGlass,
            FontSize = 14
        };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(
            _addressBox,
            _isCommandPaletteRequested
                ? $"Filter: {GetSearchSourceLabel(_selectedSearchSource)}"
                : null);
    }

    private void SetAddressBarText(string value, bool preserveUserEdit = false)
    {
        var nextValue = value ?? string.Empty;
        _addressBarText = nextValue;

        if (_isCommandPaletteRequested)
        {
            return;
        }

        if (preserveUserEdit && _isAddressBarEditing)
        {
            return;
        }

        _isAddressBarEditing = false;
        CloseSearchPopup();

        if (_addressBox is null || string.Equals(_addressBox.Text, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        _suppressAddressBoxTextChanged = true;
        _addressBox.Text = nextValue;
        _suppressAddressBoxTextChanged = false;
    }

    private void OnAddressBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseSearchPopup();
            e.Handled = true;
        }
    }

    private async void OnAddressBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
        {
            return;
        }

        var dispatcherQueue = addressBox.DispatcherQueue;
        await Task.Delay(50).ConfigureAwait(false);

        try
        {
            dispatcherQueue.TryEnqueue(HandleAddressBoxLostFocus);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The dispatcher can shut down while the delayed focus callback is pending.
        }
        catch (InvalidOperationException)
        {
            // A stopped dispatcher means the window has already completed this cleanup.
        }
    }

    private void SetCommandPaletteFilter(string value)
    {
        _paletteFilterText = value;
        ScheduleLocalSearch(value);
    }

    private void RefreshCommandPalette()
    {
        if (_searchPopup?.IsOpen != true)
        {
            return;
        }

        var query = _isCommandPaletteRequested
            ? _paletteFilterText
            : _addressBarText;
        ScheduleLocalSearch(query, preserveLimit: true);
    }

    private void SubmitCommandPaletteFilter(string value)
    {
        var query = value.Trim();
        if (query.Length == 0)
        {
            return;
        }

        CloseSearchPopup();
        Props.OnSubmitAddress(query);
    }

    private void HandleAddressBoxLostFocus()
    {
        if (_isCommandPaletteRequested)
        {
            return;
        }

        var currentAddressBox = _addressBox;
        if (currentAddressBox is null)
        {
            CloseSearchPopup();
            return;
        }

        try
        {
            var xamlRoot = currentAddressBox.XamlRoot;
            if (xamlRoot is not null)
            {
                var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if (focusedElement is not null && IsInsideSearchPopup(focusedElement))
                {
                    return;
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The window can disconnect its XAML tree during the delayed focus check.
        }
        catch (InvalidOperationException)
        {
            // Treat a disconnected address box as an already-dismissed search surface.
        }

        CloseSearchPopup();
    }

    private bool IsInsideSearchPopup(DependencyObject element)
    {
        var popupChild = _searchPopup?.Child;
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, popupChild))
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleLocalSearch(string value, bool preserveLimit = false)
    {
        if (!preserveLimit)
        {
            _localSearchLimit = PalettePageSize;
            _hasMoreLocalResults = false;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var query = value?.Trim() ?? string.Empty;

        if (query.Length < 2)
        {
            _searchResults = [];
            if (_isCommandPaletteRequested &&
                _selectedSearchSource is not AddressSearchSource.All and not AddressSearchSource.AiResults)
            {
                _ = RunLocalSearchAsync(query, cancellationToken, skipDelay: true);
            }
            else if (_isCommandPaletteRequested)
            {
                RenderSearchPopup(query);
            }
            else
            {
                CloseSearchPopup();
            }
            return;
        }

        if (_selectedSearchSource == AddressSearchSource.AiResults)
        {
            _selectedSearchSource = AddressSearchSource.All;
        }

        _ = RunLocalSearchAsync(query, cancellationToken);
    }

    private async Task RunLocalSearchAsync(
        string query,
        CancellationToken cancellationToken,
        bool skipDelay = false)
    {
        try
        {
            if (!skipDelay)
            {
                await Task.Delay(_isCommandPaletteRequested ? 90 : 260, cancellationToken);
            }

            var searchSource = _selectedSearchSource;
            var visibleLimit = searchSource == AddressSearchSource.All
                ? 8
                : Math.Clamp(_localSearchLimit, PalettePageSize, PaletteMaximumItems);
            var requestedLimit = searchSource == AddressSearchSource.All
                ? visibleLimit
                : Math.Min(visibleLimit + 1, PaletteMaximumItems);
            var searchData = await Task.Run<(
                IReadOnlyList<AddressSearchResult> Results,
                IReadOnlyList<AddressSearchCollectionGroup> CollectionGroups)>(
                () =>
                {
                    if (searchSource == AddressSearchSource.Collections)
                    {
                        var collectionGroups = AddressSearchService.SearchCollectionGroups(
                            query,
                            PaletteMaximumItems);
                        return (
                            collectionGroups.SelectMany(group => group.Items).ToArray(),
                            collectionGroups);
                    }

                    return (
                        AddressSearchService.SearchLocal(query, Props.Tabs, searchSource, requestedLimit),
                        Array.Empty<AddressSearchCollectionGroup>());
                },
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || _addressBox is null)
            {
                return;
            }

            _addressBox.DispatcherQueue.TryEnqueue(() =>
            {
                var activeQuery = _isCommandPaletteRequested
                    ? _paletteFilterText.Trim()
                    : _addressBarText.Trim();
                if (cancellationToken.IsCancellationRequested ||
                    searchSource != _selectedSearchSource ||
                    !string.Equals(query, activeQuery, StringComparison.Ordinal))
                {
                    return;
                }

                if (!_isCommandPaletteRequested && !IsAddressBoxFocused())
                {
                    CloseSearchPopup();
                    return;
                }

                _hasMoreLocalResults = searchSource is not AddressSearchSource.All and not AddressSearchSource.Collections &&
                    visibleLimit < PaletteMaximumItems &&
                    searchData.Results.Count > visibleLimit;
                _searchResults = searchSource == AddressSearchSource.Collections
                    ? searchData.Results
                    : searchData.Results.Take(visibleLimit).ToArray();
                _collectionGroups = searchData.CollectionGroups;
                if (searchSource == AddressSearchSource.Collections &&
                    (!string.Equals(_collectionFilterQuery, query, StringComparison.Ordinal) ||
                     !_collectionGroups.Any(group => string.Equals(
                         group.CollectionId,
                         _expandedCollectionId,
                         StringComparison.Ordinal))))
                {
                    _expandedCollectionId = _collectionGroups.FirstOrDefault()?.CollectionId;
                    _collectionFilterQuery = query;
                }
                _searchError = string.Empty;
                RenderSearchPopup(query);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunWebSearchAsync(string query)
    {
        if (_isWebSearchRunning || query.Length < 2)
        {
            return;
        }

        _selectedSearchSource = AddressSearchSource.AiResults;
        _isWebSearchRunning = true;
        _searchError = string.Empty;
        RenderSearchPopup(query);

        try
        {
            _searchResults = await AddressSearchService.SearchAiResultsAsync(query, 8);
            if (_searchResults.Count == 0)
            {
                _searchError = "No AI URL results were returned.";
            }
        }
        catch (Exception ex)
        {
            _searchResults = [];
            _searchError = ex.Message;
        }
        finally
        {
            _isWebSearchRunning = false;
            RenderSearchPopup(query);
        }
    }

    private void RenderSearchPopup(string query)
    {
        var addressBox = _addressBox;
        if (addressBox?.XamlRoot is null || (query.Length < 2 && !_isCommandPaletteRequested))
        {
            return;
        }

        if (_searchPopup is null)
        {
            _searchPopup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
            {
                ShouldConstrainToRootBounds = true
            };
            _searchPopup.Closed += OnSearchPopupClosed;
        }

        _searchPopup.IsLightDismissEnabled = _isCommandPaletteRequested;
        _searchPopup.XamlRoot = addressBox.XamlRoot;

        var point = addressBox.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rootWidth = addressBox.XamlRoot.Size.Width;
        var leftLimit = Math.Max(point.X, Props.IsTabsCollapsed ? 68 : 412);
        var rightLimit = rootWidth - (Props.IsChatOpen ? 544 : 12);
        var availableWidth = Math.Max(260, rightLimit - leftLimit);
        var popupWidth = Math.Min(720, Math.Min(Math.Max(260, addressBox.ActualWidth), availableWidth));
        var resultsContent = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 10,
            Width = Math.Max(0, popupWidth - 24),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (_isWebSearchRunning)
        {
            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.ProgressRing
            {
                IsActive = true,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 18)
            });
            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = $"Requesting AI results from {LinkerAiCredentialService.SelectedProvider.DisplayName}…",
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.76
            });
        }
        else if (!string.IsNullOrWhiteSpace(_searchError))
        {
            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = _searchError,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else if (query.Length < 2 && _selectedSearchSource == AddressSearchSource.All)
        {
            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Type to search tabs, history, favorites, and collections. Press Enter to use the default web search.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else if (_searchResults.Count == 0 && _selectedSearchSource != AddressSearchSource.Collections)
        {
            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = _selectedSearchSource == AddressSearchSource.AiResults
                    ? "Press AI Results → to request provider results."
                    : query.Length == 0
                        ? $"No {GetSearchSourceLabel(_selectedSearchSource).ToLowerInvariant()} to show yet."
                        : "No local matches. Press Enter for the default web search, or choose AI Results →.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else
        {
            Microsoft.UI.Xaml.UIElement resultList;
            if (_selectedSearchSource == AddressSearchSource.Collections)
            {
                resultList = BuildCollectionAccordion();
            }
            else
            {
                var resultStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 6 };
                foreach (var result in _searchResults)
                {
                    resultStack.Children.Add(BuildSearchResultRow(result));
                }

                resultList = resultStack;
            }

            resultsContent.Children.Add(new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                Content = new Microsoft.UI.Xaml.Controls.Border
                {
                    Padding = new Thickness(12, 0, 12, 4),
                    Child = resultList
                },
                MaxHeight = _hasMoreLocalResults ? 280 : 330,
                VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled
            });

            if (_hasMoreLocalResults)
            {
                var loadMoreButton = BuildSearchPill(
                    $"Load {PalettePageSize} more",
                    selected: false,
                    glyph: BrowserConstants.GlyphChevronDown);
                loadMoreButton.HorizontalAlignment = HorizontalAlignment.Center;
                loadMoreButton.Margin = new Thickness(0, 4, 0, 0);
                loadMoreButton.Click += (_, _) =>
                {
                    _localSearchLimit = Math.Min(
                        _localSearchLimit + PalettePageSize,
                        PaletteMaximumItems);
                    ScheduleLocalSearch(query, preserveLimit: true);
                };
                resultsContent.Children.Add(loadMoreButton);
            }
        }

        var shouldFocusPalette = false;
        if (_isCommandPaletteRequested)
        {
            if (_commandPaletteControl is null)
            {
                _commandPaletteControl = new CommandPaletteControl(
                    SetCommandPaletteFilter,
                    SubmitCommandPaletteFilter,
                    CloseSearchPopup);
                shouldFocusPalette = true;
            }

            _commandPaletteControl.Update(
                popupWidth,
                _paletteFilterText,
                GetCommandPalettePlaceholder(),
                BuildPaletteHeader(),
                BuildSearchSourcePills(query),
                resultsContent);
            if (!ReferenceEquals(_searchPopup.Child, _commandPaletteControl))
            {
                _searchPopup.Child = _commandPaletteControl;
            }
        }
        else
        {
            var content = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Spacing = 10,
                Children =
                {
                    BuildPaletteHeader(),
                    BuildSearchSourcePills(query),
                    resultsContent
                }
            };
            _searchPopup.Child = new Microsoft.UI.Xaml.Controls.Border
            {
                Width = popupWidth,
                MaxHeight = 520,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = BrowserMaterialTheme.ChatSurfaceBrush,
                BorderBrush = BrowserMaterialTheme.SelectedStrokeBrush,
                BorderThickness = new Thickness(1),
                Child = content,
                Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 2, 12)
            };
        }

        var centeredOffset = point.X + Math.Max(0, (addressBox.ActualWidth - popupWidth) / 2);
        _searchPopup.HorizontalOffset = Math.Clamp(centeredOffset, leftLimit, Math.Max(leftLimit, rightLimit - popupWidth));
        _searchPopup.VerticalOffset = point.Y + addressBox.ActualHeight + 6;
        _searchPopup.IsOpen = true;
        if (shouldFocusPalette && _commandPaletteControl is not null)
        {
            _ = addressBox.DispatcherQueue.TryEnqueue(_commandPaletteControl.FocusFilter);
        }
    }

    private static Microsoft.UI.Xaml.Controls.TextBox? FindAddressTextBox(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Microsoft.UI.Xaml.Controls.TextBox editor)
            {
                return editor;
            }

            var descendant = FindAddressTextBox(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private bool IsAddressBoxFocused()
    {
        var addressBox = _addressBox;
        if (addressBox is null)
        {
            return false;
        }

        return addressBox.FocusState != FocusState.Unfocused ||
            FindAddressTextBox(addressBox)?.FocusState != FocusState.Unfocused;
    }

    private Microsoft.UI.Xaml.UIElement BuildSearchSourcePills(string query)
    {
        var row = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 8)
        };

        foreach (var source in new[]
                 {
                     AddressSearchSource.All,
                     AddressSearchSource.Tabs,
                     AddressSearchSource.History,
                     AddressSearchSource.Favorites,
                     AddressSearchSource.Collections
                 })
        {
            var sourceButton = BuildSearchPill(
                source.ToString(),
                source == _selectedSearchSource,
                GetSearchSourceGlyph(source));
            sourceButton.Click += (_, _) => SelectSearchSource(source, query);
            ScrollSelectedSearchSourceIntoView(sourceButton, source == _selectedSearchSource);
            row.Children.Add(sourceButton);
        }

        var webButton = BuildSearchPill("AI Results →", _selectedSearchSource == AddressSearchSource.AiResults);
        webButton.IsEnabled = AddressSearchService.CanSearchAiResults && !_isWebSearchRunning;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(
            webButton,
            AddressSearchService.CanSearchAiResults
                ? $"Request AI-assisted URL results with {LinkerAiCredentialService.SelectedProvider.DisplayName}"
                : "Add an API key for the selected Linker provider to enable AI results");
        webButton.Click += (_, _) =>
        {
            _ = RunWebSearchAsync(query);
        };
        ScrollSelectedSearchSourceIntoView(
            webButton,
            _selectedSearchSource == AddressSearchSource.AiResults);
        row.Children.Add(webButton);

        return new Microsoft.UI.Xaml.Controls.ScrollViewer
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled,
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled,
            VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled,
            ZoomMode = Microsoft.UI.Xaml.Controls.ZoomMode.Disabled
        };
    }

    private static void ScrollSelectedSearchSourceIntoView(
        Microsoft.UI.Xaml.Controls.Button sourceButton,
        bool isSelected)
    {
        if (!isSelected)
        {
            return;
        }

        sourceButton.Loaded += (_, _) => sourceButton.StartBringIntoView(
            new BringIntoViewOptions
            {
                AnimationDesired = false,
                HorizontalAlignmentRatio = 0.5
            });
    }

    private void SelectSearchSource(AddressSearchSource source, string query)
    {
        if (!_isCommandPaletteRequested)
        {
            // A source choice made from ordinary address search becomes the same
            // persistent palette opened by the compact Library command.
            _paletteFilterText = query;
            _addressBarText = Props.SelectedTab.Url;
            _isCommandPaletteRequested = true;
            _isAddressBarEditing = true;
            if (_addressBox is not null &&
                !string.Equals(_addressBox.Text, _addressBarText, StringComparison.Ordinal))
            {
                _suppressAddressBoxTextChanged = true;
                _addressBox.Text = _addressBarText;
                _suppressAddressBoxTextChanged = false;
            }
        }

        _selectedSearchSource = source;
        _searchResults = [];
        _collectionGroups = [];
        _searchError = string.Empty;
        _hasMoreLocalResults = false;
        UpdateCommandPaletteNotification();
        RenderSearchPopup(_paletteFilterText);
        ScheduleLocalSearch(_paletteFilterText);
        if (_commandPaletteControl is not null && _addressBox is not null)
        {
            _ = _addressBox.DispatcherQueue.TryEnqueue(_commandPaletteControl.FocusFilter);
        }
    }

    private Microsoft.UI.Xaml.UIElement BuildPaletteHeader()
    {
        var sourceLabel = GetSearchSourceLabel(_selectedSearchSource);
        var detail = _selectedSearchSource == AddressSearchSource.Collections
            ? $"{_collectionGroups.Count} collection{(_collectionGroups.Count == 1 ? "" : "s")} · {_searchResults.Count} item{(_searchResults.Count == 1 ? "" : "s")}"
            : _searchResults.Count == 0
                ? $"Browse {sourceLabel.ToLowerInvariant()}"
                : $"{_searchResults.Count} shown";

        var headerGrid = new Microsoft.UI.Xaml.Controls.Grid
        {
            ColumnSpacing = 10
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sourceIcon = new FontIcon
        {
            FontFamily = BrowserConstants.IconFontFamily,
            Glyph = GetSearchSourceGlyph(_selectedSearchSource),
            FontSize = 16
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(sourceIcon, 0);
        headerGrid.Children.Add(sourceIcon);

        var labels = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = $"{(_isCommandPaletteRequested ? "Filter" : "Search")} · {sourceLabel}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 11,
                    Opacity = 0.68
                }
            }
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(labels, 1);
        headerGrid.Children.Add(labels);

        var closeButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Content = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = BrowserConstants.GlyphClose,
                FontSize = 11
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(closeButton, "Close command palette");
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(closeButton, "Close command palette");
        closeButton.Click += (_, _) => CloseSearchPopup();
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(closeButton, 2);
        headerGrid.Children.Add(closeButton);

        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(10),
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = headerGrid
        };
    }

    private static string GetSearchSourceGlyph(AddressSearchSource source) => source switch
    {
        AddressSearchSource.Tabs => BrowserConstants.GlyphTabs,
        AddressSearchSource.History => BrowserConstants.GlyphHistory,
        AddressSearchSource.Favorites => BrowserConstants.GlyphFavorite,
        AddressSearchSource.Collections => BrowserConstants.GlyphCollections,
        AddressSearchSource.AiResults => BrowserConstants.GlyphChat,
        _ => BrowserConstants.GlyphMagnifyGlass
    };

    private static string GetSearchSourceLabel(AddressSearchSource source) => source switch
    {
        AddressSearchSource.Tabs => "Tabs",
        AddressSearchSource.History => "History",
        AddressSearchSource.Favorites => "Favorites",
        AddressSearchSource.Collections => "Collections",
        AddressSearchSource.AiResults => "AI results",
        _ => "All"
    };

    private static Microsoft.UI.Xaml.Controls.Button BuildSearchPill(
        string label,
        bool selected,
        string? glyph = null) =>
        new()
        {
            Content = string.IsNullOrWhiteSpace(glyph)
                ? new TextBlock { Text = label }
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children =
                    {
                        new FontIcon
                        {
                            FontFamily = BrowserConstants.IconFontFamily,
                            Glyph = glyph,
                            FontSize = 12
                        },
                        new TextBlock { Text = label }
                    }
                },
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(9),
            Background = selected ? BrowserMaterialTheme.GlassStrongFillBrush : BrowserMaterialTheme.PillFillBrush,
            BorderBrush = selected ? BrowserMaterialTheme.SelectedStrokeBrush : BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };

    private Microsoft.UI.Xaml.UIElement BuildCollectionAccordion()
    {
        var stack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 8 };
        var startupCollectionId = TabCollectionService.GetStartupCollection()?.Id;

        if (_collectionGroups.Count == 0)
        {
            stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_paletteFilterText)
                    ? "No collections yet."
                    : "No matching collections.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76,
                Margin = new Thickness(6, 10, 6, 10)
            });
        }
        else
        {
            foreach (var group in _collectionGroups)
            {
                stack.Children.Add(BuildCollectionAccordionCard(
                    group,
                    string.Equals(group.CollectionId, startupCollectionId, StringComparison.Ordinal)));
            }
        }

        var manageButton = BuildSearchPill(
            "Manage collections",
            selected: false,
            glyph: BrowserConstants.GlyphSettings);
        manageButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        manageButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        manageButton.Margin = new Thickness(0, 4, 0, 0);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            manageButton,
            "Open full collection management");
        manageButton.Click += (_, _) =>
        {
            CloseSearchPopup();
            Props.OnOpenCollections();
        };
        stack.Children.Add(manageButton);

        return stack;
    }

    private Microsoft.UI.Xaml.UIElement BuildCollectionAccordionCard(
        AddressSearchCollectionGroup group,
        bool isStartup)
    {
        var isExpanded = string.Equals(
            _expandedCollectionId,
            group.CollectionId,
            StringComparison.Ordinal);
        var headerContent = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 10 };
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var collectionIcon = new FontIcon
        {
            FontFamily = BrowserConstants.IconFontFamily,
            Glyph = BrowserConstants.GlyphCollections,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(collectionIcon, 0);
        headerContent.Children.Add(collectionIcon);

        var itemLabel = string.IsNullOrWhiteSpace(_paletteFilterText)
            ? $"{group.ItemCount} item{(group.ItemCount == 1 ? "" : "s")}{(isStartup ? " · Startup" : "")}"
            : $"{group.ItemCount} match{(group.ItemCount == 1 ? "" : "es")}{(isStartup ? " · Startup" : "")}";
        var labels = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 1,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = group.CollectionName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                },
                new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = itemLabel,
                    FontSize = 11,
                    Opacity = 0.68
                }
            }
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(labels, 1);
        headerContent.Children.Add(labels);

        var chevron = new FontIcon
        {
            FontFamily = BrowserConstants.IconFontFamily,
            Glyph = isExpanded ? BrowserConstants.GlyphChevronUp : BrowserConstants.GlyphChevronDown,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(chevron, 2);
        headerContent.Children.Add(chevron);

        var expandButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = headerContent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 7, 8, 7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            expandButton,
            $"{(isExpanded ? "Collapse" : "Expand")} {group.CollectionName} collection");
        expandButton.Click += (_, _) =>
        {
            _expandedCollectionId = isExpanded ? null : group.CollectionId;
            RenderSearchPopup(_paletteFilterText.Trim());
        };

        var startupButton = BuildResultIconButton(
            BrowserConstants.GlyphPower,
            isStartup ? "Startup collection" : $"Use {group.CollectionName} at startup");
        startupButton.Margin = new Thickness(0, 0, 8, 0);
        startupButton.Background = isStartup
            ? BrowserMaterialTheme.GlassStrongFillBrush
            : BrowserMaterialTheme.PillFillBrush;
        startupButton.BorderBrush = isStartup
            ? BrowserMaterialTheme.SelectedStrokeBrush
            : BrowserMaterialTheme.GlassStrokeBrush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            startupButton,
            isStartup ? $"{group.CollectionName} is the startup collection" : $"Use {group.CollectionName} at startup");
        startupButton.Click += (_, _) =>
        {
            Props.OnSaveSettingValue(TabCollectionService.StartupCollectionSettingKey, group.CollectionId);
            Props.OnSaveSettingValue(TabCollectionService.StartupModeSettingKey, TabCollectionService.StartupModeCollection);
            RenderSearchPopup(_paletteFilterText.Trim());
        };

        var header = new Microsoft.UI.Xaml.Controls.Grid
        {
            ColumnSpacing = 4
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(expandButton, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(startupButton, 1);
        header.Children.Add(expandButton);
        header.Children.Add(startupButton);

        var cardContent = new Microsoft.UI.Xaml.Controls.StackPanel();
        cardContent.Children.Add(header);
        if (isExpanded)
        {
            var items = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8, 0, 8, 8)
            };

            if (group.Items.Count == 0)
            {
                items.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = "No items in this collection.",
                    Opacity = 0.7,
                    Margin = new Thickness(6, 6, 6, 6)
                });
            }
            else
            {
                foreach (var item in group.Items)
                {
                    items.Children.Add(BuildSearchResultRow(item));
                }
            }

            cardContent.Children.Add(items);
        }

        return new Microsoft.UI.Xaml.Controls.Border
        {
            CornerRadius = new CornerRadius(11),
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = isExpanded
                ? BrowserMaterialTheme.SelectedStrokeBrush
                : BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = cardContent
        };
    }

    private Microsoft.UI.Xaml.UIElement BuildSearchResultRow(AddressSearchResult result)
    {
        var grid = new Microsoft.UI.Xaml.Controls.Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(8, 6, 6, 6)
        };
        grid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = GridLength.Auto });

        var favicon = new Microsoft.UI.Xaml.Controls.Image
        {
            Width = 20,
            Height = 20,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(BrowserUrl.GetFaviconUrl(result.Url), UriKind.Absolute)),
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(favicon, 0);
        grid.Children.Add(favicon);

        var titleStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 1 };
        titleStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = result.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });
        titleStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = BuildResultDetail(result),
            FontSize = 11,
            Opacity = 0.68,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(titleStack, 1);
        grid.Children.Add(titleStack);

        var primaryButton = BuildResultIconButton(
            result.Source == AddressSearchSource.Tabs ? BrowserConstants.GlyphTabs : BrowserConstants.GlyphGo,
            result.Source == AddressSearchSource.Tabs ? "Switch to tab" : "Open in this tab");
        primaryButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(result.TabId))
            {
                Props.OnActivateTab(result.TabId);
            }
            else
            {
                Props.OnNavigateCurrentTab(result.Url);
            }

            CloseSearchPopup();
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(primaryButton, 2);
        grid.Children.Add(primaryButton);

        var newTabButton = BuildResultIconButton(BrowserConstants.GlyphAdd, "Open in new tab");
        newTabButton.Click += (_, _) =>
        {
            Props.OnOpenAddressInNewTab(result.Url);
            CloseSearchPopup();
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(newTabButton, 3);
        grid.Children.Add(newTabButton);

        return new Microsoft.UI.Xaml.Controls.Border
        {
            CornerRadius = new CornerRadius(10),
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private static Microsoft.UI.Xaml.Controls.Button BuildResultIconButton(string glyph, string tooltip)
    {
        var button = new Microsoft.UI.Xaml.Controls.Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Content = new Microsoft.UI.Xaml.Controls.FontIcon
            {
                Glyph = glyph,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 12
            }
        };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static string BuildResultDetail(AddressSearchResult result)
    {
        var host = Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : result.Url;
        var detail = result.Detail.Length > 90 ? result.Detail[..89] + "…" : result.Detail;
        return $"{detail}  ·  {host}";
    }

    private void CloseSearchPopup()
    {
        _searchCancellation?.Cancel();
        var shouldRestoreAddress = _isCommandPaletteRequested;
        _isCommandPaletteRequested = false;
        _paletteFilterText = string.Empty;
        _collectionGroups = [];
        _expandedCollectionId = null;
        _collectionFilterQuery = string.Empty;
        _selectedSearchSource = AddressSearchSource.All;
        _commandPaletteControl = null;
        RestoreAddressBoxAfterSearchClose(shouldRestoreAddress);
        var popup = _searchPopup;
        _searchPopup = null;

        if (popup is null)
        {
            return;
        }

        try
        {
            popup.IsOpen = false;
            popup.Child = null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Popup teardown can race the XAML root/window lifetime while switching tabs or closing.
        }
        catch (InvalidOperationException)
        {
            // Treat disconnected popup cleanup as best-effort.
        }
    }

    private void RestoreAddressBoxAfterSearchClose(bool shouldRestoreAddress)
    {
        var addressBox = _addressBox;
        if (addressBox is null)
        {
            return;
        }

        try
        {
            addressBox.PlaceholderText = "Search or enter web address";
            addressBox.QueryIcon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = BrowserConstants.GlyphMagnifyGlass,
                FontSize = 14
            };
            if (shouldRestoreAddress && !string.Equals(addressBox.Text, _addressBarText, StringComparison.Ordinal))
            {
                _suppressAddressBoxTextChanged = true;
                try
                {
                    addressBox.Text = _addressBarText;
                }
                finally
                {
                    _suppressAddressBoxTextChanged = false;
                }

                _isAddressBarEditing = false;
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            DetachDisconnectedAddressBox(addressBox);
        }
        catch (InvalidOperationException)
        {
            DetachDisconnectedAddressBox(addressBox);
        }
    }

    private void DetachDisconnectedAddressBox(Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
    {
        _suppressAddressBoxTextChanged = false;
        if (ReferenceEquals(_addressBox, addressBox))
        {
            _addressBox = null;
        }
    }

    private void OnSearchPopupClosed(object? sender, object e)
    {
        // Synchronize state if the popup is closed by its XAML root or window lifetime.
        // Normal palette dismissal is explicit through X, Escape, or result selection.
        if (_searchPopup is not null)
        {
            CloseSearchPopup();
        }
    }
}
