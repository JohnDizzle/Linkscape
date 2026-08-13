using LinkScape.Browser.State;
using LinkScape.Models;

namespace LinkScape.Tests;

[TestClass]
public sealed class BrowserTabActionsTests
{
    [TestMethod]
    public void Close_SelectsPreferredLastActiveTab_WhenItStillExists()
    {
        var tabs = new[]
        {
            CreateTab("one", 1),
            CreateTab("two", 2),
            CreateTab("three", 3)
        };

        var nextTabs = BrowserTabActions.Close(
            tabs,
            "three",
            "https://home.example",
            preferredSelectedId: "one",
            out var nextSelected);

        Assert.AreEqual(2, nextTabs.Length);
        Assert.IsNotNull(nextSelected);
        Assert.AreEqual("one", nextSelected.Id);
    }

    [TestMethod]
    public void Close_FallsBackToTabAbove_WhenPreferredTabIsMissing()
    {
        var tabs = new[]
        {
            CreateTab("one", 1),
            CreateTab("two", 2),
            CreateTab("three", 3)
        };

        var nextTabs = BrowserTabActions.Close(
            tabs,
            "three",
            "https://home.example",
            preferredSelectedId: "missing",
            out var nextSelected);

        Assert.AreEqual(2, nextTabs.Length);
        Assert.IsNotNull(nextSelected);
        Assert.AreEqual("two", nextSelected.Id);
    }

    private static BrowserTab CreateTab(string id, int order) =>
        new(
            id,
            $"Tab {order}",
            $"https://example.com/{order}",
            DateTime.Now,
            string.Empty,
            0,
            false,
            false,
            order,
            0,
            0);
}
