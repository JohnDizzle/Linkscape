namespace LinkScape.Tests;

[TestClass]
public sealed class SiteControlsServiceTests
{
    [TestMethod]
    public void TryGetOrigin_NormalizesPathQueryFragmentAndDefaultPort()
    {
        var result = SiteControlsService.TryGetOrigin(
            "https://Example.COM:443/account?id=7#details",
            out var origin,
            out var host);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com", origin);
        Assert.AreEqual("example.com", host);
    }

    [TestMethod]
    public void TryGetOrigin_PreservesNonDefaultPort()
    {
        Assert.IsTrue(SiteControlsService.TryGetOrigin(
            "https://localhost:8443/app",
            out var origin,
            out _));

        Assert.AreEqual("https://localhost:8443", origin);
    }

    [DataTestMethod]
    [DataRow("edge://settings")]
    [DataRow("file:///C:/temp/page.html")]
    [DataRow("not a URL")]
    [DataRow("")]
    public void TryGetOrigin_RejectsPagesWithoutWebOrigins(string value)
    {
        Assert.IsFalse(SiteControlsService.TryGetOrigin(value, out _, out _));
    }

    [DataTestMethod]
    [DataRow(0L, "No stored site data")]
    [DataRow(512L, "512 B stored")]
    [DataRow(1536L, "1.5 KB stored")]
    [DataRow(1048576L, "1 MB stored")]
    public void FormatStorageUsage_UsesCompactReadableUnits(long bytes, string expected)
    {
        Assert.AreEqual(expected, SiteControlsService.FormatStorageUsage(bytes));
    }
}
