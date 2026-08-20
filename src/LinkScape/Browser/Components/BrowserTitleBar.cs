using LinkScape.Browser;
using LinkScape.Models;
using System.Threading;
using System.Threading.Tasks;

namespace LinkScape.Browser.Components;

internal sealed class BrowserTitleBarController
{
    internal Action<string, bool>? SetAddressTextCore { get; set; }
    internal Action? OpenCommandPaletteCore { get; set; }

    public void SetAddressText(string value, bool preserveUserEdit = false) =>
        SetAddressTextCore?.Invoke(value, preserveUserEdit);

    public void OpenCommandPalette() => OpenCommandPaletteCore?.Invoke();
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
    private CancellationTokenSource? _searchCancellation;
    private AddressSearchSource _selectedSearchSource = AddressSearchSource.All;
    private IReadOnlyList<AddressSearchResult> _searchResults = [];
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

        var renderedAddressText = _isCommandPaletteRequested
            ? _paletteFilterText
            : _addressBarText;

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
            renderedAddressText,
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

        if (_isCommandPaletteRequested)
        {
            if (!string.Equals(addressBox.Text, _paletteFilterText, StringComparison.Ordinal))
            {
                _suppressAddressBoxTextChanged = true;
                addressBox.Text = _paletteFilterText;
                _suppressAddressBoxTextChanged = false;
            }

            UpdateCommandPalettePlaceholder();
            return;
        }

        if (!string.Equals(addressBox.Text, _addressBarText, StringComparison.Ordinal))
        {
            _suppressAddressBoxTextChanged = true;
            addressBox.Text = _addressBarText;
            _suppressAddressBoxTextChanged = false;
        }
    }

    private void SetAddressBarDraft(string value)
    {
        if (_suppressAddressBoxTextChanged)
        {
            return;
        }

        if (_isCommandPaletteRequested)
        {
            _paletteFilterText = value;
            ScheduleLocalSearch(value);
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

        _selectedSearchSource = AddressSearchSource.Collections;
        _searchResults = [];
        _searchError = string.Empty;
        _isCommandPaletteRequested = true;
        _isAddressBarEditing = true;
        _paletteFilterText = string.Empty;
        _suppressAddressBoxTextChanged = true;
        addressBox.Text = string.Empty;
        _suppressAddressBoxTextChanged = false;
        UpdateCommandPalettePlaceholder();
        addressBox.Focus(FocusState.Programmatic);

        var editor = FindAddressTextBox(addressBox);
        editor?.Focus(FocusState.Programmatic);
        ScheduleLocalSearch(string.Empty);
    }

    private void UpdateCommandPalettePlaceholder()
    {
        if (_addressBox is null)
        {
            return;
        }

        _addressBox.PlaceholderText = _selectedSearchSource switch
        {
            AddressSearchSource.Tabs => "Filter active tabs",
            AddressSearchSource.History => "Filter history",
            AddressSearchSource.Favorites => "Filter favorites",
            AddressSearchSource.Collections => "Filter collections",
            _ => "Search tabs, history, favorites, and collections"
        };
        _addressBox.QueryIcon = new FontIcon
        {
            FontFamily = BrowserConstants.IconFontFamily,
            Glyph = GetSearchSourceGlyph(_selectedSearchSource),
            FontSize = 14
        };
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
                await Task.Delay(260, cancellationToken);
            }

            var searchSource = _selectedSearchSource;
            var visibleLimit = searchSource == AddressSearchSource.All
                ? 8
                : Math.Clamp(_localSearchLimit, PalettePageSize, PaletteMaximumItems);
            var requestedLimit = searchSource == AddressSearchSource.All
                ? visibleLimit
                : Math.Min(visibleLimit + 1, PaletteMaximumItems);
            var results = await Task.Run(
                () => AddressSearchService.SearchLocal(query, Props.Tabs, searchSource, requestedLimit),
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

                _hasMoreLocalResults = searchSource != AddressSearchSource.All &&
                    visibleLimit < PaletteMaximumItems &&
                    results.Count > visibleLimit;
                _searchResults = results.Take(visibleLimit).ToArray();
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

        // The titlebar address box and the popup form one command-palette surface.
        // Native light dismissal treats the address box as outside the Popup and
        // closes it before the user can continue editing the active filter.
        _searchPopup.IsLightDismissEnabled = false;
        _searchPopup.XamlRoot = addressBox.XamlRoot;

        var point = addressBox.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rootWidth = addressBox.XamlRoot.Size.Width;
        var leftLimit = Math.Max(point.X, Props.IsTabsCollapsed ? 68 : 412);
        var rightLimit = rootWidth - (Props.IsChatOpen ? 544 : 12);
        var availableWidth = Math.Max(260, rightLimit - leftLimit);
        var popupWidth = Math.Min(720, Math.Min(Math.Max(260, addressBox.ActualWidth), availableWidth));
        var content = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Spacing = 10
        };
        content.Children.Add(BuildPaletteHeader());
        content.Children.Add(BuildSearchSourcePills(query));

        if (_isWebSearchRunning)
        {
            content.Children.Add(new Microsoft.UI.Xaml.Controls.ProgressRing
            {
                IsActive = true,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 18)
            });
            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = $"Requesting AI results from {LinkerAiCredentialService.SelectedProvider.DisplayName}…",
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.76
            });
        }
        else if (!string.IsNullOrWhiteSpace(_searchError))
        {
            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = _searchError,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else if (query.Length < 2 && _selectedSearchSource == AddressSearchSource.All)
        {
            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Type to search tabs, history, favorites, and collections. Press Enter to use the default web search.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else if (_searchResults.Count == 0)
        {
            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
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
            var resultStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 6 };
            if (_selectedSearchSource == AddressSearchSource.Collections)
            {
                foreach (var group in _searchResults.GroupBy(result => result.Detail))
                {
                    resultStack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                    {
                        Text = group.Key.Replace("Collections › ", string.Empty, StringComparison.Ordinal),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Opacity = 0.72,
                        Margin = new Thickness(6, 8, 6, 2)
                    });

                    foreach (var result in group)
                    {
                        resultStack.Children.Add(BuildSearchResultRow(result));
                    }
                }
            }
            else
            {
                foreach (var result in _searchResults)
                {
                    resultStack.Children.Add(BuildSearchResultRow(result));
                }
            }

            content.Children.Add(new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                Content = resultStack,
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
                content.Children.Add(loadMoreButton);
            }
        }

        var popupBorder = new Microsoft.UI.Xaml.Controls.Border
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

        _searchPopup.Child = popupBorder;
        var centeredOffset = point.X + Math.Max(0, (addressBox.ActualWidth - popupWidth) / 2);
        _searchPopup.HorizontalOffset = Math.Clamp(centeredOffset, leftLimit, Math.Max(leftLimit, rightLimit - popupWidth));
        _searchPopup.VerticalOffset = point.Y + addressBox.ActualHeight + 6;
        _searchPopup.IsOpen = true;
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
            Spacing = 6
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
        row.Children.Add(webButton);

        return row;
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
        }

        _selectedSearchSource = source;
        _searchResults = [];
        _searchError = string.Empty;
        _hasMoreLocalResults = false;
        UpdateCommandPalettePlaceholder();
        RenderSearchPopup(_paletteFilterText);
        ScheduleLocalSearch(_paletteFilterText);
    }

    private Microsoft.UI.Xaml.UIElement BuildPaletteHeader()
    {
        var sourceLabel = GetSearchSourceLabel(_selectedSearchSource);
        var detail = _searchResults.Count == 0
            ? sourceLabel == "Collections" ? "Collection contents" : $"Browse {sourceLabel.ToLowerInvariant()}"
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
                    Text = sourceLabel,
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
        _selectedSearchSource = AddressSearchSource.All;
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
