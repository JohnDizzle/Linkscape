using LinkScape.Browser;
using LinkScape.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Input;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    internal Action? RefreshLayoutCore { get; set; }
    internal Func<string, Task>? PauseMediaInTabAsyncCore { get; set; }
    internal Func<string, Task>? CaptureScrollPositionAsyncCore { get; set; }
    internal Func<Task<string?>>? CaptureActivePageImageAsyncCore { get; set; }

    public void Navigate(string tabId, string url) => NavigateCore?.Invoke(tabId, url);

    public void CloseTab(string tabId) => CloseTabCore?.Invoke(tabId);

    public void ReloadTab(string tabId) => ReloadTabCore?.Invoke(tabId);

    public void GoBack() => GoBackCore?.Invoke();

    public void GoBack(string tabId) => GoBackTabCore?.Invoke(tabId);

    public void GoForward() => GoForwardCore?.Invoke();

    public void GoForward(string tabId) => GoForwardTabCore?.Invoke(tabId);

    public void Reload() => ReloadCore?.Invoke();

    public void RefreshLayout() => RefreshLayoutCore?.Invoke();

    public Task PauseMediaInTabAsync(string tabId) =>
        PauseMediaInTabAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;

    public Task CaptureScrollPositionAsync(string tabId) =>
        CaptureScrollPositionAsyncCore?.Invoke(tabId) ?? Task.CompletedTask;

    public Task<string?> CaptureActivePageImageAsync() =>
        CaptureActivePageImageAsyncCore?.Invoke() ?? Task.FromResult<string?>(null);

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
    private static readonly TimeSpan InactiveTabSuspendDelay = TimeSpan.FromSeconds(60);
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
    private readonly object _suspendDelayGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _suspendDelayByTabId = [];
    private Microsoft.UI.Xaml.Controls.WebView2? _activeWebView;
    private Microsoft.UI.Xaml.Controls.Border? _webViewHost;
    private string? _activeWebViewTabId;

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
        Props.Controller.RefreshLayoutCore = RefreshWebViewLayout;
        Props.Controller.PauseMediaInTabAsyncCore = PauseMediaInTabAsync;
        Props.Controller.CaptureScrollPositionAsyncCore = CaptureScrollPositionAsync;
        Props.Controller.CaptureActivePageImageAsyncCore = CaptureActiveViewportAsync;

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
        ConfigureLinkerVirtualHost(core);

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
                BrowserNoticeService.Clear();

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

        AttachWebViewToHost(_webViewHost ?? host, webView);

    }

    private static void HandleCoreWebView2Initialized(
        Microsoft.UI.Xaml.Controls.WebView2 sender,
        Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        ConfigureLinkerVirtualHost(sender.CoreWebView2);
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
