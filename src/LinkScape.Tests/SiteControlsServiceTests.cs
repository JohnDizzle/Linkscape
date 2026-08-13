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

    [DataTestMethod]
    [DataRow(0.1, 100)]
    [DataRow(0.4, 100)]
    [DataRow(1.0, 100)]
    [DataRow(1.25, 125)]
    [DataRow(5.0, 500)]
    [DataRow(6.0, 500)]
    public void FormatZoomPercent_ClampsToSitePanelRange(double zoomFactor, int expected)
    {
        Assert.AreEqual(expected, SiteControlsService.FormatZoomPercent(zoomFactor));
    }

    [DataTestMethod]
    [DataRow(1, 1.0)]
    [DataRow(40, 1.0)]
    [DataRow(100, 1.0)]
    [DataRow(175, 1.75)]
    [DataRow(500, 5.0)]
    [DataRow(900, 5.0)]
    public void ToZoomFactor_ConvertsClampedPercentToWebViewFactor(int percent, double expected)
    {
        Assert.AreEqual(expected, SiteControlsService.ToZoomFactor(percent), 0.001);
    }
}
