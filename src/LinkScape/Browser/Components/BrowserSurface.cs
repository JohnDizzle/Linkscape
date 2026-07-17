using LinkScape.Browser;
using LinkScape.Models;
using System.Text.Json;
using System.Threading.Tasks;

namespace Browser.Components;

internal sealed class BrowserSurfaceController
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
    private readonly HashSet<string> _hookedWebViewTabs = [];
    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private Microsoft.UI.Xaml.Controls.Border? _webViewHost;
    private string? _activeWebViewTabId;
    private Action<string, string>? _setTitleFromCore;
    private Action<string>? _setAddressFromCore;
    private Action<bool, bool>? _setNavAvailability;
    private Action<bool>? _setLoadingStateFromCore;
    private Action? _refreshHistoryFromCore;

    public void SetCallbacks(
        Action<string, string> setTitleFromCore,
        Action<string> setAddressFromCore,
        Action<bool, bool> setNavAvailability,
        Action<bool> setLoadingStateFromCore,
        Action refreshHistoryFromCore)
    {
        _setTitleFromCore = setTitleFromCore;
        _setAddressFromCore = setAddressFromCore;
        _setNavAvailability = setNavAvailability;
        _setLoadingStateFromCore = setLoadingStateFromCore;
        _refreshHistoryFromCore = refreshHistoryFromCore;
    }

    public void SetActiveTab(string tabId)
    {
        _activeWebViewTabId = tabId;

        if (_webViewsByTabId.TryGetValue(tabId, out var webView))
        {
            _webView = webView;
        }
    }

    public bool TryGetNavigationState(string tabId, out bool canGoBack, out bool canGoForward)
    {
        canGoBack = false;
        canGoForward = false;

        if (!_webViewsByTabId.TryGetValue(tabId, out var webView) ||
            webView.CoreWebView2 is null)
        {
            return false;
        }

        canGoBack = webView.CoreWebView2.CanGoBack;
        canGoForward = webView.CoreWebView2.CanGoForward;
        return true;
    }

    public void GoBack()
    {
        _webView?.GoBack();
    }

    public void GoForward()
    {
        _webView?.GoForward();
    }

    public void RefreshLayout()
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

    public void Navigate(string tabId, string url, Action<bool> setLoadingState)
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

            setLoadingState(true);
        }
        catch
        {
            setLoadingState(false);
        }
    }

    public void Reload(string tabId, string? fallbackUrl, Action<bool> setLoadingState)
    {
        if (!_webViewsByTabId.TryGetValue(tabId, out var webView))
        {
            return;
        }

        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Reload();
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            Navigate(tabId, fallbackUrl, setLoadingState);
        }
    }

    public void CloseTab(string tabId)
    {
        if (!_webViewsByTabId.Remove(tabId, out var closedWebView))
        {
            _hookedWebViewTabs.Remove(tabId);
            return;
        }

        if (_webViewHost?.Child == closedWebView)
        {
            _webViewHost.Child = null;
        }

        closedWebView.Close();
        _hookedWebViewTabs.Remove(tabId);

        if (string.Equals(_activeWebViewTabId, tabId, StringComparison.Ordinal))
        {
            _activeWebViewTabId = null;
        }

        if (ReferenceEquals(_webView, closedWebView))
        {
            _webView = null;
        }
    }

    public async Task PauseMediaInTabAsync(string tabId)
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

    public async Task CaptureScrollPositionAsync(
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

    public async Task ShowSelectedWebViewAsync(
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

    private void AttachWebViewToHost(
        Microsoft.UI.Xaml.Controls.Border host,
        Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        host.DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_webViewHost, host) && host.Child == webView)
            {
                webView.Visibility = Visibility.Visible;
                return;
            }

            if (webView.Parent is Microsoft.UI.Xaml.Controls.Border previousHost &&
                previousHost != host)
            {
                previousHost.Child = null;
            }

            if (host.Child != webView)
            {
                host.Child = webView;
            }

            _webViewHost = host;
            webView.Visibility = Visibility.Visible;
            webView.InvalidateMeasure();
            webView.InvalidateArrange();
            webView.UpdateLayout();
        });
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

