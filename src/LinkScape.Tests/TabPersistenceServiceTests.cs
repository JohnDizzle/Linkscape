using System.Text.Json.Nodes;

[TestClass]
public sealed class TabPersistenceServiceTests
{
    private sealed record TestTab(string Id, string Title, string Url, DateTime DateTime, int VisitedCount);

    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void EnsureDatabase_AllowsSavingAndLoadingValues()
    {
        TabPersistenceService.EnsureDatabase();
        var tabs = new[]
        {
            new TestTab("tab-1", "Example", "https://example.com", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1)
        };

        TabPersistenceService.SaveTabs("tabs", tabs);
        var loaded = TabPersistenceService.LoadTabs<TestTab[]>("tabs");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded!.Length);
        Assert.AreEqual("tab-1", loaded[0].Id);
    }

    [TestMethod]
    public void LoadTabs_ReturnsDefault_WhenKeyMissing()
    {
        TabPersistenceService.EnsureDatabase();

        Assert.IsNull(TabPersistenceService.LoadTabs<TestTab[]>("missing"));
    }

    [TestMethod]
    public void UpdateTabVisit_UpdatesTimestamp_Url_AndVisitCount_WhenUrlChanged()
    {
        TabPersistenceService.EnsureDatabase();
        var originalTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        TabPersistenceService.SaveTabs("tabs", new[]
        {
            new TestTab("tab-1", "Example", "https://example.com", originalTime, 3)
        });

        TabPersistenceService.UpdateTabVisit("tabs", "tab-1", incrementVisitCount: true, newUrl: "https://example.org", urlChanged: true);
        var loaded = TabPersistenceService.LoadTabs<TestTab[]>("tabs");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded!.Length);
        Assert.AreEqual("https://example.org", loaded[0].Url);
        Assert.AreEqual(4, loaded[0].VisitedCount);
        Assert.IsTrue(loaded[0].DateTime > originalTime);
    }

    [TestMethod]
    public void UpdateTabVisit_DoesNothing_WhenKeyOrTabIsMissing()
    {
        TabPersistenceService.EnsureDatabase();
        var tabs = new[]
        {
            new TestTab("tab-1", "Example", "https://example.com", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3)
        };
        TabPersistenceService.SaveTabs("tabs", tabs);

        TabPersistenceService.UpdateTabVisit("missing", "tab-1", newUrl: "https://example.org", urlChanged: true);
        TabPersistenceService.UpdateTabVisit("tabs", "missing", newUrl: "https://example.org", urlChanged: true);
        var loaded = TabPersistenceService.LoadTabs<TestTab[]>("tabs");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("https://example.com", loaded![0].Url);
        Assert.AreEqual(3, loaded[0].VisitedCount);
    }

    [TestMethod]
    public void SaveOrReplaceTabJson_AppendsNewTab_WithDefaultVisitCount()
    {
        TabPersistenceService.EnsureDatabase();

        var node = new JsonObject
        {
            ["Id"] = "tab-1",
            ["Title"] = "Example",
            ["Url"] = "https://example.com"
        };

        TabPersistenceService.SaveOrReplaceTabJson("tabs", node);
        var loaded = TabPersistenceService.LoadTabs<TestTab[]>("tabs");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded!.Length);
        Assert.AreEqual(1, loaded[0].VisitedCount);
        Assert.IsTrue(loaded[0].DateTime > DateTime.MinValue);
    }

    [TestMethod]
    public void SaveOrReplaceTabJson_ReplacesExistingTab_AndPreservesVisitCount_WhenMissingInReplacement()
    {
        TabPersistenceService.EnsureDatabase();
        TabPersistenceService.SaveTabs("tabs", new[]
        {
            new TestTab("tab-1", "Original", "https://example.com", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5)
        });

        var replacement = new JsonObject
        {
            ["Id"] = "tab-1",
            ["Title"] = "Updated",
            ["Url"] = "https://example.org"
        };

        TabPersistenceService.SaveOrReplaceTabJson("tabs", replacement);
        var loaded = TabPersistenceService.LoadTabs<TestTab[]>("tabs");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded!.Length);
        Assert.AreEqual("Updated", loaded[0].Title);
        Assert.AreEqual("https://example.org", loaded[0].Url);
        Assert.AreEqual(5, loaded[0].VisitedCount);
    }

    [TestMethod]
    public void RemoveTabs_RemovesPersistedValue_AndReturnsExpectedResult()
    {
        TabPersistenceService.EnsureDatabase();
        TabPersistenceService.SaveTabs("tabs", new[]
        {
            new TestTab("tab-1", "Example", "https://example.com", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1)
        });

        Assert.IsFalse(TabPersistenceService.RemoveTabs(""));
        Assert.IsTrue(TabPersistenceService.RemoveTabs("tabs"));
        Assert.IsFalse(TabPersistenceService.RemoveTabs("tabs"));
        Assert.IsNull(TabPersistenceService.LoadTabs<TestTab[]>("tabs"));
    }
}
