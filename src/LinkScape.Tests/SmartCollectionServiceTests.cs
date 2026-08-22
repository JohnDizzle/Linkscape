namespace LinkScape.Tests;

[TestClass]
public sealed class SmartCollectionServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        SettingsService.EnsureDatabase();
        HistoryPersistenceService.EnsureDatabase();
        FavoritesService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();
    }

    [TestMethod]
    public void CreateOrRefresh_MergesHistoryAndFavoritesIntoTopicCollections()
    {
        HistoryPersistenceService.RecordVisit("https://github.com/JohnDizzle/Linkscape", "LinkScape");
        HistoryPersistenceService.RecordVisit("https://github.com/JohnDizzle/Linkscape", "LinkScape");
        FavoritesService.UpsertFavorite(null, "https://openai.com", "OpenAI");
        FavoritesService.UpsertFavorite(null, "https://accounts.google.com/signin", "Sign in");

        var summary = SmartCollectionService.CreateOrRefresh(rebuild: true);

        var devItems = TabCollectionService.GetItems("Smart - Dev & Docs");
        var aiItems = TabCollectionService.GetItems("Smart - AI & Research");

        Assert.AreEqual(5, summary.CollectionCount);
        Assert.IsTrue(devItems.Any(item => item.Url == "https://github.com/JohnDizzle/Linkscape"));
        Assert.IsTrue(aiItems.Any(item => item.Url == "https://openai.com"));
        Assert.IsFalse(TabCollectionService.GetCollections()
            .SelectMany(collection => TabCollectionService.GetItems(collection.Id))
            .Any(item => item.Url.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CreateOrRefresh_SynchronizesEachCollectionToRankedTopTen()
    {
        for (var index = 0; index < 12; index++)
        {
            var url = $"https://github.com/example/project-{index}";
            for (var visit = index; visit < 12; visit++)
            {
                HistoryPersistenceService.RecordVisit(url, $"Project {index}");
            }
        }

        TabCollectionService.AddOrUpdateItem(
            "Smart - Dev & Docs",
            "https://github.com/example/stale-project",
            "Stale project");

        var summary = SmartCollectionService.CreateOrRefresh(
            rebuild: false,
            itemsPerCollection: 25);
        var devItems = TabCollectionService.GetItems("Smart - Dev & Docs");

        Assert.AreEqual(SmartCollectionService.MaximumItemsPerCollection, devItems.Count);
        Assert.AreEqual(SmartCollectionService.MaximumItemsPerCollection,
            summary.CollectionItemCounts["Smart - Dev & Docs"]);
        Assert.IsTrue(devItems.Any(item => item.Url.Contains("project-0", StringComparison.Ordinal)));
        Assert.IsFalse(devItems.Any(item => item.Url.Contains("project-11", StringComparison.Ordinal)));
        Assert.IsFalse(devItems.Any(item => item.Url.Contains("stale-project", StringComparison.Ordinal)));
    }
}
