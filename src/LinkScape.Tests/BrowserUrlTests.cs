using LinkScape.Browser;

namespace LinkScape.Tests;

[TestClass]
public sealed class BrowserUrlTests
{
    [TestMethod]
    public void TryNormalizeAbsoluteUrl_RejectsEdgeInternalUrls()
    {
        Assert.IsFalse(BrowserUrl.TryNormalizeAbsoluteUrl("edge://settings", out _));
        Assert.IsFalse(BrowserUrl.TryNormalizeAbsoluteUrl("edge:settings", out _));
    }

    [TestMethod]
    public void Normalize_TreatsEdgeInternalUrlAsSearchText()
    {
        var normalized = BrowserUrl.Normalize(
            "edge://settings",
            "https://example.com",
            BrowserSearchProviders.DefaultProviderKey);

        Assert.IsTrue(normalized.StartsWith("https://www.bing.com/search?", StringComparison.Ordinal));
        Assert.IsTrue(normalized.Contains("edge%3A%2F%2Fsettings", StringComparison.Ordinal));
    }

    [TestMethod]
    public void IsBlockedInternalUrl_RecognizesEdgeSchemeOnly()
    {
        Assert.IsTrue(BrowserUrl.IsBlockedInternalUrl("edge://flags"));
        Assert.IsFalse(BrowserUrl.IsBlockedInternalUrl("https://www.microsoft.com/edge"));
    }
}
