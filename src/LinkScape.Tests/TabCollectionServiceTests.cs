[TestClass]
public sealed class TabCollectionServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        SettingsService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();
    }

    [TestMethod]
    public void AddOrUpdateItem_CreatesCollectionAndStoresItem()
    {
        var item = TabCollectionService.AddOrUpdateItem("Personal", "https://example.com/?utm_source=test", "Example");
        var collection = TabCollectionService.GetCollectionByName("personal");
        var items = TabCollectionService.GetItems("Personal");

        Assert.IsNotNull(collection);
        Assert.AreEqual(collection!.Id, item.CollectionId);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("Example", items[0].Title);
        Assert.AreEqual("https://example.com", items[0].Url);
    }

    [TestMethod]
    public void AddOrUpdateItem_UpdatesExistingUrlWithoutDuplicating()
    {
        TabCollectionService.AddOrUpdateItem("Work", "https://example.com", "Example");
        TabCollectionService.AddOrUpdateItem("Work", "https://example.com/", "Updated");

        var items = TabCollectionService.GetItems("Work");

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("Updated", items[0].Title);
    }

    [TestMethod]
    public void RenameCollection_ChangesCollectionName()
    {
        TabCollectionService.UpsertCollection("Personal");

        Assert.IsTrue(TabCollectionService.RenameCollection("Personal", "Day Off"));
        Assert.IsNull(TabCollectionService.GetCollectionByName("Personal"));
        Assert.IsNotNull(TabCollectionService.GetCollectionByName("Day Off"));
    }

    [TestMethod]
    public void SetStartupCollection_StoresStartupSettings()
    {
        var collection = TabCollectionService.UpsertCollection("Work");

        TabCollectionService.SetStartupCollection("Work");
        var startupCollection = TabCollectionService.GetStartupCollection();

        Assert.IsNotNull(startupCollection);
        Assert.AreEqual(collection.Id, startupCollection!.Id);
        Assert.AreEqual(TabCollectionService.StartupModeCollection, SettingsService.GetValue(TabCollectionService.StartupModeSettingKey));
    }
}
