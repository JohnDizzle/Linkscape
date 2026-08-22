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
}
