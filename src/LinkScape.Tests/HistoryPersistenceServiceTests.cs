[TestClass]
public sealed class HistoryPersistenceServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void EnsureDatabase_InitializesEmptyHistoryStore()
    {
        HistoryPersistenceService.EnsureDatabase();

        Assert.AreEqual(0, HistoryPersistenceService.GetRecentHistory().Count);
    }

    [TestMethod]
    public void RecordVisit_NormalizesUrl_FiltersIgnoredUrls_AndIncrementsVisitCount()
    {
        HistoryPersistenceService.EnsureDatabase();

        HistoryPersistenceService.RecordVisit("about:blank", "Ignored");
        HistoryPersistenceService.RecordVisit("file:///C:/temp/test.html", "Ignored");
        HistoryPersistenceService.RecordVisit("https://example.com/path?b=2&utm_source=newsletter&a=1#section", null);
        HistoryPersistenceService.RecordVisit("https://example.com/path?a=1&b=2", "Updated Title");

        var items = HistoryPersistenceService.GetRecentHistory();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("https://example.com/path?a=1&b=2", items[0].Url);
        Assert.AreEqual("Updated Title", items[0].Title);
        Assert.AreEqual(2, items[0].VisitCount);
    }

    [TestMethod]
    public void RecordVisit_NormalizesYouTubeUrls_ToCanonicalWatchUrl()
    {
        HistoryPersistenceService.EnsureDatabase();

        HistoryPersistenceService.RecordVisit("https://youtu.be/abc123?t=42", "Video");
        HistoryPersistenceService.RecordVisit("https://www.youtube.com/watch?v=abc123&feature=share", "Video Updated");

        var items = HistoryPersistenceService.GetRecentHistory();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("https://www.youtube.com/watch?v=abc123", items[0].Url);
        Assert.AreEqual(2, items[0].VisitCount);
        Assert.AreEqual("Video Updated", items[0].Title);
    }

    [TestMethod]
    public void GetRecentHistory_AndSearchHistory_ReturnExpectedItems()
    {
        HistoryPersistenceService.EnsureDatabase();

        HistoryPersistenceService.RecordVisit("https://example.com", "Example");
        Thread.Sleep(20);
        HistoryPersistenceService.RecordVisit("https://github.com/linkscape", "GitHub Repo");

        var recent = HistoryPersistenceService.GetRecentHistory(1);
        var byTitle = HistoryPersistenceService.SearchHistory("repo", 10);
        var byUrl = HistoryPersistenceService.SearchHistory("example.com", 10);
        var all = HistoryPersistenceService.SearchHistory(null, 10);

        Assert.AreEqual(1, recent.Count);
        Assert.AreEqual("https://github.com/linkscape", recent[0].Url);
        Assert.AreEqual(1, byTitle.Count);
        Assert.AreEqual("https://github.com/linkscape", byTitle[0].Url);
        Assert.AreEqual(1, byUrl.Count);
        Assert.AreEqual("https://example.com", byUrl[0].Url);
        Assert.AreEqual(2, all.Count);
    }

    [TestMethod]
    public void GetMostVisited_ReturnsItemsOrderedByVisitCount()
    {
        HistoryPersistenceService.EnsureDatabase();

        HistoryPersistenceService.RecordVisit("https://one.example", "One");
        HistoryPersistenceService.RecordVisit("https://two.example", "Two");
        HistoryPersistenceService.RecordVisit("https://two.example", "Two");
        HistoryPersistenceService.RecordVisit("https://three.example", "Three");
        HistoryPersistenceService.RecordVisit("https://three.example", "Three");
        HistoryPersistenceService.RecordVisit("https://three.example", "Three");

        var items = HistoryPersistenceService.GetMostVisited(2);

        Assert.AreEqual(2, items.Count);
        Assert.AreEqual("https://three.example", items[0].Url);
        Assert.AreEqual(3, items[0].VisitCount);
        Assert.AreEqual("https://two.example", items[1].Url);
        Assert.AreEqual(2, items[1].VisitCount);
    }

    [TestMethod]
    public void UpsertImportedHistory_InsertsAndMergesItems()
    {
        HistoryPersistenceService.EnsureDatabase();

        var firstVisited = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var lastVisited = new DateTime(2024, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        HistoryPersistenceService.UpsertImportedHistory(new[]
        {
            new HistoryItem("https://example.com?a=1&utm_source=newsletter", "Initial", firstVisited, lastVisited, 2)
        });

        HistoryPersistenceService.UpsertImportedHistory(new[]
        {
            new HistoryItem("https://example.com?a=1", "Updated", firstVisited.AddDays(-1), lastVisited.AddDays(1), 5)
        });

        var items = HistoryPersistenceService.GetRecentHistory();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("https://example.com/?a=1", items[0].Url);
        Assert.AreEqual("Updated", items[0].Title);
        Assert.AreEqual(firstVisited.AddDays(-1), items[0].FirstVisitedAt);
        Assert.AreEqual(lastVisited.AddDays(1), items[0].LastVisitedAt);
        Assert.AreEqual(5, items[0].VisitCount);
    }

    [TestMethod]
    public void DeleteUrl_AndClearHistory_RemovePersistedItems()
    {
        HistoryPersistenceService.EnsureDatabase();
        HistoryPersistenceService.RecordVisit("https://example.com?a=1&utm_medium=email", "Example");
        HistoryPersistenceService.RecordVisit("https://github.com", "GitHub");

        HistoryPersistenceService.DeleteUrl("https://example.com?a=1");

        var remaining = HistoryPersistenceService.GetRecentHistory();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("https://github.com", remaining[0].Url);

        HistoryPersistenceService.ClearHistory();

        Assert.AreEqual(0, HistoryPersistenceService.GetRecentHistory().Count);
    }
}
