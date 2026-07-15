using LinkScape.Models;

namespace LinkScape.Browser.State;

internal static class BrowserTabActions
{
    public static BrowserTab[] Replace(
        BrowserTab[] tabs,
        string id,
        Func<BrowserTab, BrowserTab> updater,
        out bool changed)
    {
        changed = false;

        var nextTabs = new BrowserTab[tabs.Length];

        for (var i = 0; i < tabs.Length; i++)
        {
            var tab = tabs[i];

            if (tab.Id != id)
            {
                nextTabs[i] = tab;
                continue;
            }

            var updated = updater(tab);
            nextTabs[i] = updated;
            changed = !Equals(updated, tab);
        }

        return nextTabs;
    }

    public static BrowserTab[] Add(
        BrowserTab[] tabs,
        string url,
        out BrowserTab newTab,
        int visitCount = 0)
    {
        newTab = BrowserTab.CreateNew(tabs.Length + 1, url, visitCount);
        return [.. tabs, newTab];
    }

    public static BrowserTab[] Close(
        BrowserTab[] tabs,
        string selectedId,
        out BrowserTab? nextSelected)
    {
        nextSelected = null;

        if (tabs.Length <= 1)
        {
            return tabs;
        }

        var index = Array.FindIndex(tabs, tab => tab.Id == selectedId);

        if (index < 0)
        {
            return tabs;
        }

        var nextTabs = tabs.Where(tab => tab.Id != selectedId).ToArray();
        var nextIndex = Math.Clamp(index - 1, 0, nextTabs.Length - 1);

        nextSelected = nextTabs[nextIndex];
        return nextTabs;
    }

    public static BrowserTab ToggleFavorite(BrowserTab tab)
    {
        var favoriteId = tab.IsFavorite
            ? ""
            : Guid.NewGuid().ToString("N");

        return tab with
        {
            IsFavorite = !tab.IsFavorite,
            FavoriteId = favoriteId,
            DateTime = DateTime.Now
        };
    }
}