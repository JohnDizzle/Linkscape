using LinkScape.Models;

namespace LinkScape.Browser.State;

internal static class BrowserSessionStore
{
    public static BrowserSessionState SetTabs(BrowserSessionState state, BrowserTab[] tabs) =>
        state with { Tabs = tabs };

    public static BrowserSessionState SetSelectedTab(BrowserSessionState state, string selectedTabId) =>
        state with { SelectedTabId = selectedTabId };

    public static BrowserSessionState SetTabsCollapsed(BrowserSessionState state, bool isTabsCollapsed) =>
        state with { IsTabsCollapsed = isTabsCollapsed };

    public static BrowserSessionState SetNavAvailability(BrowserSessionState state, bool canGoBack, bool canGoForward) =>
        state with { CanGoBack = canGoBack, CanGoForward = canGoForward };

    public static BrowserSessionState SetLoading(BrowserSessionState state, bool isLoading) =>
        state with { IsLoading = isLoading };

    public static BrowserSessionState SetSelectedSearchProvider(BrowserSessionState state, string selectedSearchProviderKey) =>
        state with { SelectedSearchProviderKey = selectedSearchProviderKey };

    public static BrowserSessionState SetActiveCommandCenterSection(BrowserSessionState state, string activeCommandCenterSection) =>
        state with { ActiveCommandCenterSection = activeCommandCenterSection };

    public static BrowserSessionState SetCommandCenterExpanded(BrowserSessionState state, bool isCommandCenterExpanded) =>
        state with { IsCommandCenterExpanded = isCommandCenterExpanded };

    public static BrowserSessionState SetRailTabsExpanded(BrowserSessionState state, bool isRailTabsExpanded) =>
        state with { IsRailTabsExpanded = isRailTabsExpanded };

    public static BrowserSessionState DismissCommandCenter(BrowserSessionState state)
    {
        return state with
        {
            IsCommandCenterExpanded = false,
            ActiveCommandCenterSection = string.Empty
        };
    }

    public static BrowserSessionState MaximizeRailTabs(BrowserSessionState state) =>
        state with
        {
            IsCommandCenterExpanded = false,
            ActiveCommandCenterSection = string.Empty,
            IsRailTabsExpanded = true
        };

    public static BrowserSessionState MinimizeRailTabs(BrowserSessionState state) =>
        state with
        {
            IsCommandCenterExpanded = false,
            ActiveCommandCenterSection = string.Empty,
            IsRailTabsExpanded = false
        };
}
