using LinkScape.Models;

namespace LinkScape.Browser.State;

internal sealed record BrowserSessionState(
    BrowserTab[] Tabs,
    string SelectedTabId,
    bool IsTabsCollapsed,
    bool CanGoBack,
    bool CanGoForward,
    bool IsLoading,
    string SelectedSearchProviderKey,
    string ActiveCommandCenterSection,
    bool IsCommandCenterExpanded,
    bool IsRailTabsExpanded,
    bool IsChatOpen)
{
    public bool IsCommandCenterOpen => !string.IsNullOrWhiteSpace(ActiveCommandCenterSection);

    public static BrowserSessionState Create(
        BrowserTab[] tabs,
        string selectedTabId,
        string selectedSearchProviderKey) =>
        new(
            tabs,
            selectedTabId,
            IsTabsCollapsed: true,
            CanGoBack: false,
            CanGoForward: false,
            IsLoading: false,
            SelectedSearchProviderKey: selectedSearchProviderKey,
            ActiveCommandCenterSection: string.Empty,
            IsCommandCenterExpanded: false,
            IsRailTabsExpanded: true,
            IsChatOpen: false);
}
