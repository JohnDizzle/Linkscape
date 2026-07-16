[TestClass]
public sealed class FavoritesServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void EnsureDatabase_InitializesEmptyFavoritesStore()
    {
        FavoritesService.EnsureDatabase();

        Assert.AreEqual(0, FavoritesService.GetFavorites().Count);
    }

    [TestMethod]
    public void UpsertFavorite_ThrowsForBlankUrl()
    {
        FavoritesService.EnsureDatabase();

        Assert.ThrowsException<ArgumentException>(() => FavoritesService.UpsertFavorite(null, " ", "Title"));
    }

    [TestMethod]
    public void UpsertFavorite_CreatesFavorite_WhenIdMissing()
    {
        FavoritesService.EnsureDatabase();

        var favorite = FavoritesService.UpsertFavorite(null, "https://example.com", " Example ");
        var stored = FavoritesService.GetFavorite(favorite.Id);

        Assert.IsFalse(string.IsNullOrWhiteSpace(favorite.Id));
        Assert.IsNotNull(stored);
        Assert.AreEqual("https://example.com", stored!.Url);
        Assert.AreEqual("Example", stored.Title);
    }

    [TestMethod]
    public void UpsertFavorite_UpdatesExistingFavorite_AndPreservesCreatedAt()
    {
        FavoritesService.EnsureDatabase();

        var created = FavoritesService.UpsertFavorite("fav-1", "https://example.com", "Original");
        Thread.Sleep(20);
        var updated = FavoritesService.UpsertFavorite("fav-1", "https://example.org", null);

        Assert.AreEqual(created.Id, updated.Id);
        Assert.AreEqual(created.CreatedAt, updated.CreatedAt);
        Assert.IsTrue(updated.UpdatedAt >= created.UpdatedAt);
        Assert.AreEqual("https://example.org", updated.Url);
        Assert.AreEqual("https://example.org", updated.Title);
    }

    [TestMethod]
    public void RemoveFavorite_ReturnsExpectedResult()
    {
        FavoritesService.EnsureDatabase();
        var favorite = FavoritesService.UpsertFavorite("fav-1", "https://example.com", "Example");

        Assert.IsFalse(FavoritesService.RemoveFavorite(""));
        Assert.IsFalse(FavoritesService.RemoveFavorite("missing"));
        Assert.IsTrue(FavoritesService.RemoveFavorite(favorite.Id));
        Assert.IsNull(FavoritesService.GetFavorite(favorite.Id));
    }

    [TestMethod]
    public void ClearFavorites_RemovesAllFavorites()
    {
        FavoritesService.EnsureDatabase();
        FavoritesService.UpsertFavorite("fav-1", "https://example.com", "Example");
        FavoritesService.UpsertFavorite("fav-2", "https://github.com", "GitHub");

        FavoritesService.ClearFavorites();

        Assert.AreEqual(0, FavoritesService.GetFavorites().Count);
    }

    [TestMethod]
    public void SearchFavorites_FiltersByTitleOrUrl_AndReturnsAllForBlankQuery()
    {
        FavoritesService.EnsureDatabase();
        FavoritesService.UpsertFavorite("fav-1", "https://example.com", "Example Home");
        FavoritesService.UpsertFavorite("fav-2", "https://github.com/linkscape", "GitHub Repo");

        var byTitle = FavoritesService.SearchFavorites("repo");
        var byUrl = FavoritesService.SearchFavorites("example.com");
        var all = FavoritesService.SearchFavorites(null);

        Assert.AreEqual(1, byTitle.Count);
        Assert.AreEqual("fav-2", byTitle[0].Id);
        Assert.AreEqual(1, byUrl.Count);
        Assert.AreEqual("fav-1", byUrl[0].Id);
        Assert.AreEqual(2, all.Count);
    }
}
