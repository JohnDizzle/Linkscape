using LinkScape.Browser;
using LinkScape.Models;
using LinkScape.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Input;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Browser.Components;

internal sealed class BrowserWebViewHostController
{
    internal Action<string, string>? NavigateCore { get; set; }
    internal Action<string>? CloseTabCore { get; set; }
    internal Action<string>? ReloadTabCore { get; set; }
    internal Action? GoBackCore { get; set; }
    internal Action<string>? GoBackTabCore { get; set; }
    internal Action? GoForwardCore { get; set; }
    internal Action<string>? GoForwardTabCore { get; set; }
    internal Action? ReloadCore { get; set; }
    internal Action<string>? ReloadWithNoticeCore { get; set; }
    internal Action? RefreshLayoutCore { get; set; }
    internal Func<string, Task>? PauseMediaInTabAsyncCore { get; set; }
    internal Func<string, Task>? CaptureScrollPositionAsyncCore { get; set; }
    internal Func<Task<string?>>? CaptureActivePageImageAsyncCore { get; set; }
    internal Func<string, bool, Task>? SetExtensionEnabledAsyncCore { get; set; }
    internal Func<CoreWebView2BrowsingDataKinds, Task>? ClearBrowsingDataAsyncCore { get; set; }

    public void Navigate(string tabId, string url) => NavigateCore?.Invoke(tabId, url);

    public void CloseTab(string tabId) => CloseTabCore?.Invoke(tabId);

    public void ReloadTab(string tabId) => ReloadTabCore?.Invoke(tabId);

    public void GoBack() => GoBackCore?.Invoke();

    public void GoBack(string tabId) => GoBackTabCore?.Invoke(tabId);

    public void GoForward() => GoForwardCore?.Invoke();

    public void GoForward(string tabId) => GoForwardTabCore?.Invoke(tabId);

    public void Reload() => ReloadCore?.Invoke();

    public void ReloadWithNotice(string message) => ReloadWithNoticeCore?.Invoke(message);

    public void RefreshLayout() => RefreshLayoutCore?.Invoke();

    public Task PauseMediaInTabAsync(string tabId) =>
        PauseMediaInTabAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;

    public Task CaptureScrollPositionAsync(string tabId) =>
        CaptureScrollPositionAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;

    public Task<string?> CaptureActivePageImageAsync() =>
        CaptureActivePageImageAsyncCore?.Invoke() ?? Task.FromResult<string?>(null);

    public Task SetExtensionEnabledAsync(string extensionId, bool enabled) =>
        SetExtensionEnabledAsyncCore?.Invoke(extensionId, enabled) ?? Task.CompletedTask;

    public Task ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds dataKinds) =>
        ClearBrowsingDataAsyncCore?.Invoke(dataKinds) ?? Task.CompletedTask;

}

internal sealed record BrowserWebViewHostProps(
    BrowserWebViewHostController Controller,
    BrowserTab SelectedTab,
    Action OnHostTapped,
    Action<string, Func<BrowserTab, BrowserTab>> UpdateTab,
    Action<string> OpenUriInNewTab,
    Action<string, string> SetTitleFromCore,
    Action<bool, bool> SetNavAvailability,
    Action<string> SetAddressFromCore,
    Action<bool> SetLoadingStateFromCore,
    Action RefreshHistoryFromCore);

internal sealed class BrowserWebViewHost : Component<BrowserWebViewHostProps>
{
    private static readonly TimeSpan InactiveTabSuspendDelay = TimeSpan.FromSeconds(180);
    private const string LinkerVirtualHostName = "linker.local";
    private const string LinkerAssetsFolderName = "Assets";
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

    private readonly Dictionary<string, Microsoft.UI.Xaml.Controls.WebView2> _webViewsByTabId = [];
    private readonly Dictionary<string, BrowserTab> _tabSnapshotsById = [];
    private readonly HashSet<string> _hookedWebViewTabs = [];
    private readonly HashSet<string> _pendingInitialScrollRestoreTabs = [];
    private readonly object _suspendDelayGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _suspendDelayByTabId = [];
    private Microsoft.UI.Xaml.Controls.WebView2? _activeWebView;
    private Microsoft.UI.Xaml.Controls.Border? _webViewHost;
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _peekPopup;
    private Microsoft.UI.Xaml.Controls.WebView2? _peekWebView;
    private string? _activeWebViewTabId;
    private string? _pendingNavigationNotice;
    private static readonly Lazy<Task<CoreWebView2Environment>> BrowserEnvironment =
        new(CreateBrowserEnvironmentAsync);

    protected override bool ShouldUpdate(BrowserWebViewHostProps? oldProps, BrowserWebViewHostProps? newProps)
    {
        if (oldProps is null || newProps is null)
        {
            return true;
        }

        return !ReferenceEquals(oldProps.Controller, newProps.Controller) ||
            !string.Equals(oldProps.SelectedTab.Id, newProps.SelectedTab.Id, StringComparison.Ordinal) ||
            !string.Equals(oldProps.SelectedTab.Url, newProps.SelectedTab.Url, StringComparison.Ordinal) ||
            !string.Equals(oldProps.SelectedTab.Title, newProps.SelectedTab.Title, StringComparison.Ordinal) ||
            oldProps.SelectedTab.IsFavorite != newProps.SelectedTab.IsFavorite ||
            oldProps.SelectedTab.IsSleeping != newProps.SelectedTab.IsSleeping ||
            oldProps.SelectedTab.ScrollX != newProps.SelectedTab.ScrollX ||
            oldProps.SelectedTab.ScrollY != newProps.SelectedTab.ScrollY;
    }

    public override Element Render()
    {
        _tabSnapshotsById[Props.SelectedTab.Id] = Props.SelectedTab;

        Props.Controller.NavigateCore = ApplyWebViewSource;
        Props.Controller.CloseTabCore = CloseTab;
        Props.Controller.ReloadTabCore = ReloadTab;
        Props.Controller.GoBackCore = () => _activeWebView?.GoBack();
        Props.Controller.GoBackTabCore = tabId => _webViewsByTabId.GetValueOrDefault(tabId)?.GoBack();
        Props.Controller.GoForwardCore = () => _activeWebView?.GoForward();
        Props.Controller.GoForwardTabCore = tabId => _webViewsByTabId.GetValueOrDefault(tabId)?.GoForward();
        Props.Controller.ReloadCore = () => _activeWebView?.CoreWebView2?.Reload();
        Props.Controller.ReloadWithNoticeCore = message =>
        {
            _pendingNavigationNotice = message;
            _activeWebView?.CoreWebView2?.Reload();
        };
        Props.Controller.RefreshLayoutCore = RefreshWebViewLayout;
        Props.Controller.PauseMediaInTabAsyncCore = PauseMediaInTabAsync;
        Props.Controller.CaptureScrollPositionAsyncCore = CaptureScrollPositionAsync;
        Props.Controller.CaptureActivePageImageAsyncCore = CaptureActiveViewportAsync;
        Props.Controller.SetExtensionEnabledAsyncCore = SetExtensionEnabledAsync;
        Props.Controller.ClearBrowsingDataAsyncCore = ClearBrowsingDataAsync;

        return Border(null)
            .Set(host =>
            {
                _webViewHost = host;
                EnsureHostStructure(host);

                host.Tapped -= HandleHostTapped;
                host.Tapped += HandleHostTapped;

                _ = ShowSelectedWebViewAsync(host, Props.SelectedTab, Props.UpdateTab, Props.OpenUriInNewTab);
            })
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
            .MinHeight(300);
    }

    private void HandleHostTapped(object? sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        Props.OnHostTapped();
    }

    private void EnsureHostStructure(Microsoft.UI.Xaml.Controls.Border host)
    {
        if (host.Child is Microsoft.UI.Xaml.Controls.Border)
        {
            host.Child = null;
        }
    }

    private BrowserTab GetTabSnapshot(string tabId, BrowserTab fallback)
    {
        return _tabSnapshotsById.TryGetValue(tabId, out var tab)
            ? tab
            : fallback;
    }

    private void ApplyWebViewSource(string tabId, string url)
    {
        if (!_webViewsByTabId.TryGetValue(tabId, out var webView))
        {
            return;
        }

        if (BrowserUrl.IsBlockedInternalUrl(url))
        {
            BrowserNoticeService.Show("Edge internal URLs are not available in LinkScape.");
            Props.SetLoadingStateFromCore(false);
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

            Props.SetLoadingStateFromCore(true);
        }
        catch
        {
            Props.SetLoadingStateFromCore(false);
        }
    }

    private void CloseTab(string tabId)
    {
        CancelPendingSuspend(tabId);
        _tabSnapshotsById.Remove(tabId);

        if (_webViewsByTabId.Remove(tabId, out var closedWebView))
        {
            if (_webViewHost?.Child == closedWebView)
            {
                _webViewHost.Child = null;
            }

            closedWebView.Close();
        }

        _hookedWebViewTabs.Remove(tabId);
        _pendingInitialScrollRestoreTabs.Remove(tabId);

        if (string.Equals(_activeWebViewTabId, tabId, StringComparison.Ordinal))
        {
            _activeWebViewTabId = null;
            _activeWebView = null;
        }
    }

    private void ReloadTab(string tabId)
    {
        if (_webViewsByTabId.TryGetValue(tabId, out var webView))
        {
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.Reload();
                return;
            }

            var url = _tabSnapshotsById.TryGetValue(tabId, out var tab)
                ? tab.Url
                : null;

            if (!string.IsNullOrWhiteSpace(url))
            {
                ApplyWebViewSource(tabId, url);
            }
        }
    }

    private async Task ShowSelectedWebViewAsync(
        Microsoft.UI.Xaml.Controls.Border host,
        BrowserTab tab,
        Action<string, Func<BrowserTab, BrowserTab>> updateTab,
        Action<string> openUriInNewTab)
    {
        var isNewWebView = false;
        var previousTabId = _activeWebViewTabId;
        if (!string.IsNullOrWhiteSpace(previousTabId) &&
            !string.Equals(previousTabId, tab.Id, StringComparison.Ordinal))
        {
            PrepareInactiveTab(previousTabId);
        }

        CancelPendingSuspend(tab.Id);

        if (!_webViewsByTabId.TryGetValue(tab.Id, out var webView))
        {
            webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 300
            };
            webView.CoreWebView2Initialized += HandleCoreWebView2Initialized;

            _webViewsByTabId[tab.Id] = webView;
            _pendingInitialScrollRestoreTabs.Add(tab.Id);
            isNewWebView = true;
            Props.SetNavAvailability(false, false);
        }

        _activeWebView = webView;
        _activeWebViewTabId = tab.Id;

        if (webView.CoreWebView2 is null)
        {
            await webView.EnsureCoreWebView2Async(await BrowserEnvironment.Value);
        }

        var core = webView.CoreWebView2;
        ConfigureLinkerVirtualHost(core);

        if (core is not null)
        {
            await BrowserExtensionService.MaintainExtensionsAsync(core.Profile);
        }

        if (core?.IsSuspended == true)
        {
            core.Resume();
        }

        SetTabSleepingState(tab.Id, false);

        if (core is not null && _hookedWebViewTabs.Add(tab.Id))
        {
            void SyncTabFromCore(bool completeLoading)
            {
                var currentTab = GetTabSnapshot(tab.Id, tab);
                var currentUrl = core.Source;

                if (string.IsNullOrWhiteSpace(currentUrl))
                {
                    currentUrl = currentTab.Url;
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
                            : current.VisitedCount,
                        ScrollX = urlChanged ? 0 : current.ScrollX,
                        ScrollY = urlChanged ? 0 : current.ScrollY
                    };
                });

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
                    }
                    catch
                    {
                    }

                    Props.RefreshHistoryFromCore();
                }

                if (string.Equals(_activeWebViewTabId, tab.Id, StringComparison.Ordinal))
                {
                    if (completeLoading)
                    {
                        Props.SetLoadingStateFromCore(false);
                    }

                    Props.SetAddressFromCore(currentUrl);
                    Props.SetNavAvailability(core.CanGoBack, core.CanGoForward);
                }
            }

            webView.NavigationStarting += (_, args) =>
            {
                BrowserNoticeService.Clear();

                if (BrowserUrl.IsBlockedInternalUrl(args.Uri))
                {
                    args.Cancel = true;
                    BrowserNoticeService.Show("Edge internal URLs are not available in LinkScape.");
                    Props.SetLoadingStateFromCore(false);
                    return;
                }

                if (string.Equals(_activeWebViewTabId, tab.Id, StringComparison.Ordinal))
                {
                    Props.SetLoadingStateFromCore(true);
                    Props.SetNavAvailability(core.CanGoBack, core.CanGoForward);
                }
            };

            webView.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess && IsNoNetworkFailure(args.WebErrorStatus))
                {
                    BrowserNoticeService.Show("No network connection. Check your internet access and try again.");
                }

                SyncTabFromCore(completeLoading: true);

                var currentTab = GetTabSnapshot(tab.Id, tab);
                if (_pendingInitialScrollRestoreTabs.Remove(tab.Id))
                {
                    await RestoreScrollPositionAsync(
                        tab.Id,
                        currentTab.ScrollX,
                        currentTab.ScrollY);
                }

                if (!string.IsNullOrWhiteSpace(_pendingNavigationNotice))
                {
                    var message = _pendingNavigationNotice;
                    _pendingNavigationNotice = null;
                    BrowserNoticeService.Show(message, "info");
                }

            };

            core.HistoryChanged += (_, _) =>
            {
                SyncTabFromCore(completeLoading: false);
            };

            core.NewWindowRequested += (_, e) =>
            {
                if (BrowserUrl.IsBlockedInternalUrl(e.Uri))
                {
                    e.Handled = true;
                    BrowserNoticeService.Show("Edge internal URLs are not available in LinkScape.");
                    return;
                }

                openUriInNewTab(e.Uri);
                e.Handled = true;
            };

            core.DocumentTitleChanged += (_, _) =>
            {
                var title = core.DocumentTitle;

                if (!string.IsNullOrWhiteSpace(title))
                {
                    Props.SetTitleFromCore(tab.Id, title);
                }
            };

            core.ContextMenuRequested += (sender, args) =>
            {
                if (!args.ContextMenuTarget.HasLinkUri ||
                    !TryGetPeekUri(args.ContextMenuTarget.LinkUri, out var peekUri))
                {
                    return;
                }

                var location = args.Location;
                var peekItem = core.Environment.CreateContextMenuItem(
                    "Peek link",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);
                peekItem.CustomItemSelected += (sender, eventArgs) =>
                {
                    webView.DispatcherQueue.TryEnqueue(() =>
                        _ = ShowPeekAsync(peekUri, location.X, location.Y));
                };

                args.MenuItems.Insert(0, peekItem);
                args.MenuItems.Insert(1, core.Environment.CreateContextMenuItem(
                    string.Empty,
                    null,
                    CoreWebView2ContextMenuItemKind.Separator));
            };
        }

        if (isNewWebView)
        {
            var initialUrl = BrowserUrl.IsBlockedInternalUrl(tab.Url)
                ? BrowserConstants.HomeUrl
                : tab.Url;
            webView.Source = new Uri(initialUrl);
        }
        else if (core is not null)
        {
            Props.SetNavAvailability(core.CanGoBack, core.CanGoForward);
        }

        AttachWebViewToHost(_webViewHost ?? host, webView);

    }

    private static void HandleCoreWebView2Initialized(
        Microsoft.UI.Xaml.Controls.WebView2 sender,
        Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        ConfigureLinkerVirtualHost(sender.CoreWebView2);
    }

    private static Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(
            Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "WebView2");
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true, 
         };

        return CoreWebView2Environment.CreateWithOptionsAsync(
            string.Empty,
            userDataFolder,
            options).AsTask();
    }

    private static bool TryGetPeekUri(string? rawUrl, out Uri uri)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var candidate) &&
            candidate.Scheme is "http" or "https" &&
            !BrowserUrl.IsBlockedInternalUrl(candidate.AbsoluteUri))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private async Task ShowPeekAsync(Uri uri, double linkX, double linkY)
    {
        ClosePeek();

        var host = _webViewHost;
        if (host?.XamlRoot is null)
        {
            return;
        }

        const double peekWidth = 640;
        const double peekHeight = 480;
        var rootSize = host.XamlRoot.Size;
        var hostOrigin = host.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
        var horizontalOffset = Math.Clamp(
            hostOrigin.X + linkX + 16,
            12,
            Math.Max(12, rootSize.Width - peekWidth - 12));
        var verticalOffset = Math.Clamp(
            hostOrigin.Y + linkY + 16,
            12,
            Math.Max(12, rootSize.Height - peekHeight - 12));

        var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
        {
            XamlRoot = host.XamlRoot,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            IsLightDismissEnabled = true,
            ShouldConstrainToRootBounds = true
        };

        var peekWebView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 44, 0, 0)
        };

        var title = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = $"Peek  •  {uri.Host}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 10, 0)
        };

        var openButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = new Microsoft.UI.Xaml.Controls.FontIcon
            {
                Glyph = BrowserConstants.GlyphTabs,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 13
            },
            Width = 32,
            Height = 32,
            MinWidth = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(openButton, "Open in tab");
        openButton.Click += (_, _) =>
        {
            var currentUri = peekWebView.CoreWebView2?.Source;
            Props.OpenUriInNewTab(string.IsNullOrWhiteSpace(currentUri)
                ? uri.AbsoluteUri
                : currentUri);
            ClosePeek();
        };

        var closeButton = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = new Microsoft.UI.Xaml.Controls.FontIcon
            {
                Glyph = BrowserConstants.GlyphClose,
                FontFamily = BrowserConstants.IconFontFamily,
                FontSize = 12
            },
            Width = 32,
            Height = 32,
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(closeButton, "Close peek");
        closeButton.Click += (_, _) => ClosePeek();

        var toolbar = new Microsoft.UI.Xaml.Controls.Grid
        {
            Height = 44,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8, 6, 6, 6),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2C, 0x3F, 0x56))
        };
        toolbar.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        toolbar.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
        {
            Width = GridLength.Auto
        });
        toolbar.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
        {
            Width = GridLength.Auto
        });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(title, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(openButton, 1);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(closeButton, 2);
        toolbar.Children.Add(title);
        toolbar.Children.Add(openButton);
        toolbar.Children.Add(closeButton);

        var layout = new Microsoft.UI.Xaml.Controls.Grid();
        layout.Children.Add(peekWebView);
        layout.Children.Add(toolbar);
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(toolbar, 1);

        popup.Child = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = peekWidth,
            Height = peekHeight,
            Background = BrowserConstants.CardBackgroundFillColorDefaultBrush,
            BorderBrush = BrowserConstants.AccentFillColorDefaultBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = layout
        };
        popup.Closed += (_, _) => ClosePeek();

        _peekPopup = popup;
        _peekWebView = peekWebView;
        popup.IsOpen = true;

        await peekWebView.EnsureCoreWebView2Async(await BrowserEnvironment.Value);
        if (_peekPopup != popup || !popup.IsOpen)
        {
            return;
        }

        ConfigureLinkerVirtualHost(peekWebView.CoreWebView2);
        peekWebView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (TryGetPeekUri(args.Uri, out var newTabUri))
            {
                Props.OpenUriInNewTab(newTabUri.AbsoluteUri);
                ClosePeek();
            }
        };
        peekWebView.Source = uri;
    }

    private void ClosePeek()
    {
        var popup = _peekPopup;
        _peekPopup = null;

        if (popup is not null)
        {
            popup.IsOpen = false;
            popup.Child = null;
        }

        var webView = _peekWebView;
        _peekWebView = null;
        webView?.Close();
    }

    private async Task SetExtensionEnabledAsync(string extensionId, bool enabled)
    {
        var definition = BrowserExtensionService.Extensions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, extensionId, StringComparison.Ordinal));

        if (definition is null)
        {
            throw new ArgumentException("Unknown browser extension.", nameof(extensionId));
        }

        var core = _activeWebView?.CoreWebView2;
        if (core is null)
        {
            throw new InvalidOperationException("Open a browser tab before changing extensions.");
        }

        await BrowserExtensionService.SetEnabledAsync(core.Profile, definition, enabled);
    }

    private Task ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds dataKinds)
    {
        var core = _activeWebView?.CoreWebView2;
        if (core is null)
        {
            throw new InvalidOperationException("Open a browser tab before clearing browsing data.");
        }

        return core.Profile.ClearBrowsingDataAsync(dataKinds).AsTask();
    }

    private async Task<string?> CaptureActiveViewportAsync()
    {
        var webView = _activeWebView;
        var tabId = _activeWebViewTabId;
        if (webView?.CoreWebView2 is null || string.IsNullOrWhiteSpace(tabId))
        {
            return null;
        }

        CancelPendingSuspend(tabId);
        string? imageDataUrl = null;

        try
        {
            await RunOnWebViewThreadAsync(webView, async () =>
            {
                if (!ReferenceEquals(_activeWebView, webView) ||
                    !string.Equals(_activeWebViewTabId, tabId, StringComparison.Ordinal) ||
                    webView.CoreWebView2 is null)
                {
                    return;
                }

                if (webView.CoreWebView2.IsSuspended)
                {
                    webView.CoreWebView2.Resume();
                    SetTabSleepingState(tabId, false);
                }

                var captureParameters = new JsonObject
                {
                    ["format"] = "jpeg",
                    ["quality"] = 70,
                    ["fromSurface"] = true,
                    ["captureBeyondViewport"] = false,
                    ["optimizeForSpeed"] = true
                };
                var screenshotJson = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot",
                    captureParameters.ToJsonString());
                var screenshot = JsonNode.Parse(screenshotJson)?["data"]?.GetValue<string>();
                imageDataUrl = string.IsNullOrWhiteSpace(screenshot)
                    ? null
                    : $"data:image/jpeg;base64,{screenshot}";
            });

            return imageDataUrl;
        }
        catch (Exception ex)
        {
            LocalMcpDiagnostics.Trace("PageViewport", $"Capture failed: {ex.Message}");
            return null;
        }
    }

    private static void ConfigureLinkerVirtualHost(CoreWebView2? core)
    {
        if (core is null)
        {
            return;
        }

        var assetsFolder = System.IO.Path.Combine(AppContext.BaseDirectory, LinkerAssetsFolderName);
        if (!System.IO.Directory.Exists(assetsFolder))
        {
            return;
        }

        core.SetVirtualHostNameToFolderMapping(
            LinkerVirtualHostName,
            assetsFolder,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    private static void AttachWebViewToHost(
        Microsoft.UI.Xaml.Controls.Border host,
        Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        host.DispatcherQueue.TryEnqueue(() =>
        {
            if (host.Child == webView)
            {
                webView.Visibility = Visibility.Visible;
                return;
            }

            if (webView.Parent is Microsoft.UI.Xaml.Controls.Border previousHost &&
                previousHost != host)
            {
                previousHost.Child = null;
            }

            host.Child = webView;

            webView.Visibility = Visibility.Visible;
            webView.InvalidateMeasure();
            webView.InvalidateArrange();

            if (webView.IsLoaded)
            {
                webView.UpdateLayout();
            }
        });
    }

    private void RefreshWebViewLayout()
    {
        if (_activeWebView is null)
        {
            return;
        }

        _activeWebView.DispatcherQueue.TryEnqueue(() =>
        {
            _activeWebView.InvalidateMeasure();
            _activeWebView.InvalidateArrange();

            if (_activeWebView.IsLoaded)
            {
                _activeWebView.UpdateLayout();
            }
        });
    }

    private static bool IsNoNetworkFailure(CoreWebView2WebErrorStatus status)
    {
        return status is
            CoreWebView2WebErrorStatus.HostNameNotResolved or
            CoreWebView2WebErrorStatus.CannotConnect or
            CoreWebView2WebErrorStatus.ServerUnreachable;
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

    private void PrepareInactiveTab(string tabId)
    {
        if (!_webViewsByTabId.TryGetValue(tabId, out var webView))
        {
            return;
        }

        webView.Visibility = Visibility.Collapsed;
        _ = PauseMediaInTabAsync(tabId);
        ScheduleSuspend(tabId, webView);
    }

    private void ScheduleSuspend(string tabId, Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        CancelPendingSuspend(tabId);

        var cancellation = new CancellationTokenSource();
        lock (_suspendDelayGate)
        {
            _suspendDelayByTabId[tabId] = cancellation;
        }

        _ = SuspendAfterDelayAsync(tabId, webView, cancellation);
    }

    private async Task SuspendAfterDelayAsync(
        string tabId,
        Microsoft.UI.Xaml.Controls.WebView2 webView,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(InactiveTabSuspendDelay, cancellation.Token).ConfigureAwait(false);
            await RunOnWebViewThreadAsync(webView, async () =>
            {
                if (cancellation.IsCancellationRequested ||
                    string.Equals(_activeWebViewTabId, tabId, StringComparison.Ordinal) ||
                    webView.Visibility == Visibility.Visible ||
                    webView.CoreWebView2 is null)
                {
                    return;
                }

                // Pause again immediately before suspension in case the page started
                // media through a delayed script after the tab was first hidden.
                await PauseMediaInTabAsync(tabId);

                var suspended = await webView.CoreWebView2.TrySuspendAsync();
                if (cancellation.IsCancellationRequested ||
                    string.Equals(_activeWebViewTabId, tabId, StringComparison.Ordinal))
                {
                    if (webView.CoreWebView2.IsSuspended)
                    {
                        webView.CoreWebView2.Resume();
                    }

                    SetTabSleepingState(tabId, false);
                    return;
                }

                SetTabSleepingState(tabId, suspended && webView.CoreWebView2.IsSuspended);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LocalMcpDiagnostics.Trace("TabSleep", $"Suspend failed for tab {tabId}: {ex.Message}");
            await RunOnWebViewThreadAsync(webView, () =>
            {
                SetTabSleepingState(tabId, false);
                return Task.CompletedTask;
            });
        }
        finally
        {
            lock (_suspendDelayGate)
            {
                if (_suspendDelayByTabId.TryGetValue(tabId, out var current) &&
                    ReferenceEquals(current, cancellation))
                {
                    _suspendDelayByTabId.Remove(tabId);
                }
            }

            cancellation.Dispose();
        }
    }

    private static Task RunOnWebViewThreadAsync(
        Microsoft.UI.Xaml.Controls.WebView2 webView,
        Func<Task> action)
    {
        if (webView.DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!webView.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            completion.TrySetException(new InvalidOperationException("The WebView dispatcher is unavailable."));
        }

        return completion.Task;
    }

    private void CancelPendingSuspend(string tabId)
    {
        CancellationTokenSource? cancellation;
        lock (_suspendDelayGate)
        {
            _suspendDelayByTabId.Remove(tabId, out cancellation);
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
        }
    }

    private void SetTabSleepingState(string tabId, bool isSleeping)
    {
        if (_tabSnapshotsById.TryGetValue(tabId, out var snapshot))
        {
            if (snapshot.IsSleeping == isSleeping)
            {
                return;
            }

            _tabSnapshotsById[tabId] = snapshot with { IsSleeping = isSleeping };
        }

        Props.UpdateTab(tabId, current => current.IsSleeping == isSleeping
            ? current
            : current with { IsSleeping = isSleeping });
    }

    private async Task CaptureScrollPositionAsync(string tabId)
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

            Props.UpdateTab(tabId, tab => tab with
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
}
