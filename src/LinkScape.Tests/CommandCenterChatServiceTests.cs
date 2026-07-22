[TestClass]
public sealed class CommandCenterChatServiceTests
{
    [DataTestMethod]
    [DataRow("search for github in my favorites", BrowserDataToolService.FavoritesSearchToolName, "github")]
    [DataRow("find Microsoft docs in history", BrowserDataToolService.HistorySearchToolName, "Microsoft docs")]
    [DataRow("search history for github", BrowserDataToolService.HistorySearchToolName, "github")]
    [DataRow("search for github in my tabs", BrowserDataToolService.TabsSearchToolName, "github")]
    public void TryParseBrowserSearchPrompt_ExtractsSourceAndQuery(string prompt, string expectedToolName, string expectedQuery)
    {
        var parsed = CommandCenterChatService.TryParseBrowserSearchPrompt(prompt, out var toolName, out var query);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expectedToolName, toolName);
        Assert.AreEqual(expectedQuery, query);
    }

    [TestMethod]
    public void TryParseBrowserSearchPrompt_ReturnsFalse_WhenSourceIsNotExplicit()
    {
        var parsed = CommandCenterChatService.TryParseBrowserSearchPrompt("search for github", out var toolName, out var query);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, toolName);
        Assert.AreEqual(string.Empty, query);
    }

    [TestMethod]
    public void TrySelectToolName_SelectsTabsSearch_ForTabSearchPrompt()
    {
        var selected = BrowserDataToolService.TrySelectToolName("search for github in my tabs", out var toolName);

        Assert.IsTrue(selected);
        Assert.AreEqual(BrowserDataToolService.TabsSearchToolName, toolName);
    }

    [TestMethod]
    public void TrySelectToolName_SelectsHistoryGroup_WhenPromptMentionsFavoritesAndCollections()
    {
        var selected = BrowserDataToolService.TrySelectToolName("report history by month with favorites and collections", out var toolName);

        Assert.IsTrue(selected);
        Assert.AreEqual(BrowserDataToolService.HistoryGroupToolName, toolName);
    }

    [TestMethod]
    public void TrySelectToolName_SelectsHistoryGroup_WhenAggregatePromptIsMisspelled()
    {
        var selected = BrowserDataToolService.TrySelectToolName("aggregrate visited pages by month with collections", out var toolName);

        Assert.IsTrue(selected);
        Assert.AreEqual(BrowserDataToolService.HistoryGroupToolName, toolName);
    }

    [TestMethod]
    public void TrySelectToolName_SelectsHistoryPeriod_ForBasicThisYearReport()
    {
        var selected = BrowserDataToolService.TrySelectToolName("history report for discord this year", out var toolName);

        Assert.IsTrue(selected);
        Assert.AreEqual(BrowserDataToolService.HistoryPeriodToolName, toolName);
    }
}
