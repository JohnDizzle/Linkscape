using LinkScape.Browser.Components;

namespace LinkScape.Tests;

[TestClass]
public sealed class BrowserTitleBarLayoutTests
{
    [TestMethod]
    public void UseCompactTitleBar_UsesCompactLayoutBelowBreakpoint()
    {
        Assert.IsTrue(BrowserChrome.UseCompactTitleBar(BrowserChrome.CompactTitleBarBreakpoint - 1));
    }

    [TestMethod]
    public void UseCompactTitleBar_UsesWideLayoutAtBreakpoint()
    {
        Assert.IsFalse(BrowserChrome.UseCompactTitleBar(BrowserChrome.CompactTitleBarBreakpoint));
    }
}
