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

    [TestMethod]
    public void TryGetExternalProtocolUri_RecognizesMailtoLinks()
    {
        var raw = "mailto:?subject=Join%20Teams%20meeting&body=Use%20the%20link%20below";

        var result = BrowserUrl.TryGetExternalProtocolUri(raw, out var uri);

        Assert.IsTrue(result);
        Assert.AreEqual("mailto", uri.Scheme);
        Assert.AreEqual(raw, uri.AbsoluteUri);
    }

    [DataTestMethod]
    [DataRow("https://teams.live.com/meet/9346834446049")]
    [DataRow("http://example.com")]
    [DataRow("file:///C:/temp/page.html")]
    [DataRow("edge://settings")]
    [DataRow("javascript:alert(1)")]
    [DataRow("")]
    public void TryGetExternalProtocolUri_RejectsBrowserHandledOrBlockedUrls(string value)
    {
        Assert.IsFalse(BrowserUrl.TryGetExternalProtocolUri(value, out _));
    }

    [TestMethod]
    public void GetFaviconUrl_UsesAppLogoForInternalPages()
    {
        var faviconUrl = BrowserUrl.GetFaviconUrl("https://linker.local/Updates/index.html?version=1.0.16.0");

        Assert.AreEqual(BrowserUrl.AppLogoFaviconUrl, faviconUrl);
    }

    [TestMethod]
    public void GetFaviconUrl_UsesDomainFaviconForExternalPages()
    {
        var faviconUrl = BrowserUrl.GetFaviconUrl("https://example.com/path");

        Assert.AreEqual("https://www.google.com/s2/favicons?sz=32&domain=example.com", faviconUrl);
    }
}
