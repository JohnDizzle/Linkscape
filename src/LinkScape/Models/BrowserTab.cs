using LinkScape.Browser;

namespace LinkScape.Models;

internal sealed record BrowserTab(
    string Id,
    string Title,
    string Url,
    DateTime DateTime,
    string FavoriteId,
    int VisitedCount,
    bool IsFavorite,
    bool IsHomeTab,
    int Order,
    double ScrollX,
    double ScrollY) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;

    public static BrowserTab CreateHome() =>
        new(
            Guid.NewGuid().ToString("N"),
            "Home",
            BrowserConstants.HomeUrl,
            DateTime.Now,
            "",
            0,
            false,
            true,
            0,
            0,
            0);

    public static BrowserTab CreateNew(int index, string url, int visitCount = 0) =>
        new(
            Guid.NewGuid().ToString("N"),
            "Loading...",
            url,
            DateTime.Now,
            "",
            visitCount,
            false,
            false,
            index,
            0,
            0);
}