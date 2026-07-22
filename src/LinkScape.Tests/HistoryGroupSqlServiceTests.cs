[TestClass]
public sealed class HistoryGroupSqlServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        HistoryPersistenceService.EnsureDatabase();
        FavoritesService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();
    }

    [TestMethod]
    public void Query_GroupsHistoryAndRelatesFavoritesAndCollectionsByUrl()
    {
        HistoryPersistenceService.RecordVisit("https://example.com/a", "Example A");
        HistoryPersistenceService.RecordVisit("https://example.com/a", "Example A");
        HistoryPersistenceService.RecordVisit("https://example.com/b", "Example B");
        FavoritesService.UpsertFavorite(null, "https://example.com/a", "Favorite title");
        TabCollectionService.AddOrUpdateItem("Research", "https://example.com/a", "Collection title");
        TabCollectionService.AddOrUpdateItem("Research", "https://example.com/b", "Collection title");

        var report = HistoryGroupSqlService.Query(new HistoryGroupSqlRequest(
            "month",
            "visits,favorites,collections",
            "current",
            "visits",
            10));

        Assert.AreEqual(1, report.Rows.Count);
        Assert.AreEqual(2, report.Rows[0].PageCount);
        Assert.AreEqual(3, report.Rows[0].VisitCount);
        Assert.AreEqual(1, report.Rows[0].FavoriteCount);
        Assert.AreEqual(2, report.Rows[0].CollectionLinkCount);
    }

    [TestMethod]
    public void FromArguments_ParsesNaturalHistoryGroupPrompt()
    {
        var request = HistoryGroupSqlService.FromArguments(new Dictionary<string, string>
        {
            ["prompt"] = "report history by year with favorites and collections",
            ["limit"] = "5"
        });

        Assert.AreEqual("year", request.GroupBy);
        Assert.AreEqual("visits,favorites,collections", request.Include);
        Assert.AreEqual("current", request.State);
        Assert.AreEqual("favorites", request.SortBy);
        Assert.AreEqual(5, request.Limit);
    }

    [TestMethod]
    public void FromArguments_DefaultsToVisitsOnlyAndExtractsSearchGroup()
    {
        var request = HistoryGroupSqlService.FromArguments(new Dictionary<string, string>
        {
            ["prompt"] = "report history by year for discord"
        });

        Assert.AreEqual("year", request.GroupBy);
        Assert.AreEqual("visits", request.Include);
        CollectionAssert.AreEqual(new[] { "discord" }, request.SearchTerms?.ToArray());
    }

    [TestMethod]
    public void Query_FiltersByUrlParameter()
    {
        HistoryPersistenceService.RecordVisit("https://example.com/a", "Example A");
        HistoryPersistenceService.RecordVisit("https://example.com/a", "Example A");
        HistoryPersistenceService.RecordVisit("https://example.com/b", "Example B");

        var report = HistoryGroupSqlService.Query(new HistoryGroupSqlRequest(
            "month",
            "visits",
            "current",
            "visits",
            10,
            Url: "https://example.com/a"));

        Assert.AreEqual(1, report.Rows.Count);
        Assert.AreEqual(1, report.Rows[0].PageCount);
        Assert.AreEqual(2, report.Rows[0].VisitCount);
    }

    [TestMethod]
    public void Query_FiltersBySearchTerms()
    {
        HistoryPersistenceService.RecordVisit("https://discord.com/channels", "Discord");
        HistoryPersistenceService.RecordVisit("https://github.com", "GitHub");

        var report = HistoryGroupSqlService.Query(new HistoryGroupSqlRequest(
            "month",
            "visits",
            "current",
            "visits",
            10,
            SearchTerms: ["discord"]));

        Assert.AreEqual(1, report.Rows.Count);
        Assert.AreEqual(1, report.Rows[0].PageCount);
        Assert.AreEqual(1, report.Rows[0].VisitCount);
    }
}
