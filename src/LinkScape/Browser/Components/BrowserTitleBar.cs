using LinkScape.Browser;
using LinkScape.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Browser.Components;

internal sealed class BrowserTitleBarController
{
    internal Action<string, bool>? SetAddressTextCore { get; set; }

    public void SetAddressText(string value, bool preserveUserEdit = false) =>
        SetAddressTextCore?.Invoke(value, preserveUserEdit);
}

internal sealed record BrowserTitleBarProps(
    BrowserTitleBarController Controller,
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
    Action<string, string> OnSaveSettingValue,
    Action OnAddTab,
    Action OnCloseTab);

internal sealed class BrowserTitleBar : Component<BrowserTitleBarProps>
{
    private Microsoft.UI.Xaml.Controls.AutoSuggestBox? _addressBox;
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _searchPopup;
    private CancellationTokenSource? _searchCancellation;
    private AddressSearchSource _selectedSearchSource = AddressSearchSource.All;
    private IReadOnlyList<AddressSearchResult> _searchResults = [];
    private bool _isWebSearchRunning;
    private string _searchError = string.Empty;
    private string _addressBarText = string.Empty;
    private bool _isAddressBarEditing;
    private bool _suppressAddressBoxTextChanged;
    private bool _isInitialized;

    public override Element Render()
    {
        if (!_isInitialized)
        {
            _addressBarText = Props.SelectedTab.Url;
            _isInitialized = true;
        }

        Props.Controller.SetAddressTextCore = SetAddressBarText;

        return BrowserChrome.BuildTitleBar(
            Props.SelectedTab,
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
            Props.SelectedSearchProviderKey,
            Props.SearchProviders,
            Props.OnSelectSearchProvider,
            Props.OnSetCurrentPageAsHome,
            Props.OnToggleFavorite,
            Props.OnSaveSettingValue,
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
    }

    private void SetAddressBarDraft(string value)
    {
        if (_suppressAddressBoxTextChanged)
        {
            return;
        }

        _isAddressBarEditing = true;
        _addressBarText = value;
        if (IsAddressBoxFocused())
        {
            ScheduleLocalSearch(value);
        }
    }

    private void SetAddressBarText(string value, bool preserveUserEdit = false)
    {
        var nextValue = value ?? string.Empty;
        _addressBarText = nextValue;

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
        await Task.Delay(50);

        if (_addressBox?.XamlRoot is null)
        {
            CloseSearchPopup();
            return;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_addressBox.XamlRoot) as DependencyObject;
        if (focusedElement is not null && IsInsideSearchPopup(focusedElement))
        {
            return;
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

    private void ScheduleLocalSearch(string value)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var query = value?.Trim() ?? string.Empty;

        if (query.Length < 2)
        {
            CloseSearchPopup();
            return;
        }

        if (_selectedSearchSource == AddressSearchSource.AiResults)
        {
            _selectedSearchSource = AddressSearchSource.All;
        }

        _ = RunLocalSearchAsync(query, cancellationToken);
    }

    private async Task RunLocalSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(260, cancellationToken);
            var results = await Task.Run(
                () => AddressSearchService.SearchLocal(query, Props.Tabs, _selectedSearchSource),
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || _addressBox is null)
            {
                return;
            }

            _addressBox.DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsAddressBoxFocused())
                {
                    CloseSearchPopup();
                    return;
                }

                _searchResults = results;
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
        if (addressBox?.XamlRoot is null || query.Length < 2)
        {
            return;
        }

        _searchPopup ??= new Microsoft.UI.Xaml.Controls.Primitives.Popup
        {
            // Light-dismiss popups move keyboard focus into the popup when they open.
            // Keep this non-modal so address-bar typing remains uninterrupted.
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = true
        };
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
        else if (_searchResults.Count == 0)
        {
            content.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = _selectedSearchSource == AddressSearchSource.AiResults
                    ? "Press AI Results → to request provider results."
                    : "No local matches. Choose AI Results → to ask your configured provider.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76,
                Margin = new Thickness(4, 10, 4, 10)
            });
        }
        else
        {
            var resultStack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 6 };
            foreach (var result in _searchResults)
            {
                resultStack.Children.Add(BuildSearchResultRow(result));
            }

            content.Children.Add(new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                Content = resultStack,
                MaxHeight = 430,
                VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled
            });
        }

        var popupBorder = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = popupWidth,
            MaxHeight = 520,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(16),
            Background = BrowserMaterialTheme.ChatSurfaceBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = content,
            Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow()
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
                     AddressSearchSource.Favorites
                 })
        {
            var sourceButton = BuildSearchPill(source.ToString(), source == _selectedSearchSource);
            sourceButton.Click += (_, _) =>
            {
                _selectedSearchSource = source;
                ScheduleLocalSearch(query);
            };
            row.Children.Add(sourceButton);
        }

        var webButton = BuildSearchPill("AI Results →", _selectedSearchSource == AddressSearchSource.AiResults);
        webButton.IsEnabled = AddressSearchService.CanSearchAiResults && !_isWebSearchRunning;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(
            webButton,
            AddressSearchService.CanSearchAiResults
                ? $"Request AI-assisted URL results with {LinkerAiCredentialService.SelectedProvider.DisplayName}"
                : "Add an API key for the selected Linker provider to enable AI results");
        webButton.Click += (_, _) => _ = RunWebSearchAsync(query);
        row.Children.Add(webButton);

        return row;
    }

    private static Microsoft.UI.Xaml.Controls.Button BuildSearchPill(string label, bool selected) =>
        new()
        {
            Content = label,
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
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(BrowserUrl.GetDomainFaviconUrl(result.Url), UriKind.Absolute)),
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
        if (_searchPopup is not null)
        {
            _searchPopup.IsOpen = false;
        }
    }
}
