using Microsoft.Data.Sqlite;

[TestClass]
public sealed class LinkScapeCachePathsTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void CacheDirectory_UsesOverrideDirectory_AndCreatesIt()
    {
        var cacheDirectory = LinkScapeCachePaths.CacheDirectory;

        Assert.AreEqual(TestCacheScope.RootPath, cacheDirectory);
        Assert.IsTrue(Directory.Exists(cacheDirectory));
    }

    [TestMethod]
    public void GetDatabaseConnectionString_ReturnsPathInsideCacheDirectory()
    {
        var connectionString = LinkScapeCachePaths.GetDatabaseConnectionString("sample.db");
        var builder = new SqliteConnectionStringBuilder(connectionString);

        Assert.AreEqual(Path.Combine(TestCacheScope.RootPath, "sample.db"), builder.DataSource);
    }
}
