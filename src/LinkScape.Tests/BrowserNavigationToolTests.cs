[TestClass]
public sealed class BrowserNavigationToolTests
{
    [TestMethod]
    public void GetTools_ContainsAllBrowserNavigationTools()
    {
        var toolNames = LocalMcpToolRouter.GetTools()
            .Select(tool => tool.ToolName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.TabsList));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.TabsFind));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.TabsActivate));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.Navigate));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.GoBack));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.GoForward));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.Reload));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.GoHome));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.HomeGet));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.HomeSet));
        Assert.IsTrue(toolNames.Contains(BrowserNavigationToolNames.TabsOpen));
    }

    [TestMethod]
    public void IsNavigationTool_RecognizesOnlyNavigationTools()
    {
        Assert.IsTrue(BrowserNavigationToolNames.IsNavigationTool(BrowserNavigationToolNames.Navigate));
        Assert.IsFalse(BrowserNavigationToolNames.IsNavigationTool(BrowserDataToolService.FavoritesSearchToolName));
    }
}
