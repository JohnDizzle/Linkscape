using LinkScape.Browser;
using LinkScape.Models;
using System.Text.Json;
using System.Threading.Tasks;

namespace Browser.Components;

internal sealed class BrowserWebViewHostController
{
    internal Action<string, string>? NavigateCore { get; set; }
    internal Action<string>? CloseTabCore { get; set; }
    internal Action<string>? ReloadTabCore { get; set; }
    internal Action? GoBackCore { get; set; }
    internal Action? GoForwardCore { get; set; }
    internal Action? ReloadCore { get; set; }
    internal Action? RefreshLayoutCore { get; set; }
    internal Func<string, Task>? PauseMediaInTabAsyncCore { get; set; }
    internal Func<string, Task>? CaptureScrollPositionAsyncCore { get; set; }

    public void Navigate(string tabId, string url) => NavigateCore?.Invoke(tabId, url);

    public void CloseTab(string tabId) => CloseTabCore?.Invoke(tabId);

    public void ReloadTab(string tabId) => ReloadTabCore?.Invoke(tabId);

    public void GoBack() => GoBackCore?.Invoke();

    public void GoForward() => GoForwardCore?.Invoke();

    public void Reload() => ReloadCore?.Invoke();

    public void RefreshLayout() => RefreshLayoutCore?.Invoke();

    public Task PauseMediaInTabAsync(string tabId) =>
        PauseMediaInTabAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;

    public Task CaptureScrollPositionAsync(string tabId) =>
        CaptureScrollPositionAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;
}

internal sealed record BrowserWebViewHostProps(
    BrowserWebViewHostController Controller,
    BrowserTab SelectedTab,
    bool IsCommandCenterOpen,
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
    private Microsoft.UI.Xaml.Controls.WebView2? _activeWebView;
    private Microsoft.UI.Xaml.Controls.Border? _webViewHost;
    private string? _activeWebViewTabId;

    public override Element Render()
    {
        _tabSnapshotsById[Props.SelectedTab.Id] = Props.SelectedTab;

        Props.Controller.NavigateCore = ApplyWebViewSource;
        Props.Controller.CloseTabCore = CloseTab;
        Props.Controller.ReloadTabCore = ReloadTab;
        Props.Controller.GoBackCore = () => _activeWebView?.GoBack();
        Props.Controller.GoForwardCore = () => _activeWebView?.GoForward();
        Props.Controller.ReloadCore = () => _activeWebView?.CoreWebView2?.Reload();
        Props.Controller.RefreshLayoutCore = RefreshWebViewLayout;
        Props.Controller.PauseMediaInTabAsyncCore = PauseMediaInTabAsync;
        Props.Controller.CaptureScrollPositionAsyncCore = CaptureScrollPositionAsync;

        return Border(null)
            .Set(host =>
            {
                _webViewHost = host;

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
            Props.SetNavAvailability(false, false);
        }

        _activeWebView = webView;
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
                            : current.VisitedCount
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

            webView.NavigationStarting += (_, _) =>
            {
                if (string.Equals(_activeWebViewTabId, tab.Id, StringComparison.Ordinal))
                {
                    Props.SetLoadingStateFromCore(true);
                    Props.SetNavAvailability(core.CanGoBack, core.CanGoForward);
                }
            };

            webView.NavigationCompleted += async (_, _) =>
            {
                SyncTabFromCore(completeLoading: true);

                var currentTab = GetTabSnapshot(tab.Id, tab);
                await RestoreScrollPositionAsync(tab.Id, currentTab.ScrollX, currentTab.ScrollY);
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
                    Props.SetTitleFromCore(tab.Id, title);
                }
            };
        }

        if (isNewWebView)
        {
            webView.Source = new Uri(tab.Url);
        }
        else if (core is not null)
        {
            Props.SetNavAvailability(core.CanGoBack, core.CanGoForward);
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
            _activeWebView.UpdateLayout();
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
