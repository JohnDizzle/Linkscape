namespace LinkScape.Tests;

[TestClass]
public sealed class AddressSearchServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void SearchLocal_FavoritesPrioritizesMostVisitedMatches()
    {
        FavoritesService.EnsureDatabase();
        HistoryPersistenceService.EnsureDatabase();
        FavoritesService.UpsertFavorite(null, "https://less-used.example", "Less used");
        FavoritesService.UpsertFavorite(null, "https://most-used.example", "Most used");
        HistoryPersistenceService.RecordVisit("https://less-used.example", "Less used");
        HistoryPersistenceService.RecordVisit("https://most-used.example", "Most used");
        HistoryPersistenceService.RecordVisit("https://most-used.example", "Most used");
        HistoryPersistenceService.RecordVisit("https://most-used.example", "Most used");

        var results = AddressSearchService.SearchLocal(
            string.Empty,
            [],
            AddressSearchSource.Favorites,
            10);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("https://most-used.example", results[0].Url);
        Assert.AreEqual("https://less-used.example", results[1].Url);
    }

    [TestMethod]
    public void SearchCollectionGroups_IncludesEmptyCollectionsAndFiltersItems()
    {
        TabCollectionService.EnsureDatabase();
        TabCollectionService.UpsertCollection("Empty");
        TabCollectionService.AddOrUpdateItem("Work", "https://github.com/linkscape", "LinkScape");
        TabCollectionService.AddOrUpdateItem("Work", "https://example.com", "Example");

        var allGroups = AddressSearchService.SearchCollectionGroups(string.Empty);
        var filteredGroups = AddressSearchService.SearchCollectionGroups("linkscape");

        Assert.AreEqual(2, allGroups.Count);
        Assert.IsTrue(allGroups.Any(group => group.CollectionName == "Empty" && group.ItemCount == 0));
        Assert.AreEqual(1, filteredGroups.Count);
        Assert.AreEqual("Work", filteredGroups[0].CollectionName);
        Assert.AreEqual(1, filteredGroups[0].ItemCount);
        Assert.AreEqual("https://github.com/linkscape", filteredGroups[0].Items[0].Url);
    }

    [TestMethod]
    public void ParseAiResults_AcceptsJsonCodeFenceAndCapsResults()
    {
        const string json = """
            ```json
            [
              {"title":"OpenAI","url":"https://openai.com","snippet":"AI research"},
              {"title":"Invalid","url":"javascript:alert(1)","snippet":"Ignore"},
              {"title":"Microsoft","url":"https://microsoft.com","snippet":"Software"}
            ]
            ```
            """;

        var results = AddressSearchService.ParseAiResults(json, 5);

        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(result => result.Source == AddressSearchSource.AiResults));
        Assert.AreEqual("https://openai.com", results[0].Url);
    }

    [TestMethod]
    public void ParseAiResults_AcceptsAzureStyleProseAroundJson()
    {
        const string response = """
            Here are several useful results:
            [
              {"title":"Azure","url":"https://azure.microsoft.com","snippet":"Cloud platform"}
            ]
            I hope this helps.
            """;

        var results = AddressSearchService.ParseAiResults(response, 5);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Azure", results[0].Title);
    }

    [TestMethod]
    public void ParseAiResults_FallsBackToMarkdownLinks()
    {
        const string response = "- [Microsoft Learn](https://learn.microsoft.com) — Documentation";

        var results = AddressSearchService.ParseAiResults(response, 5);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("https://learn.microsoft.com", results[0].Url);
    }
}
