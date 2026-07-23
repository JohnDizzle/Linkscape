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

    [DataTestMethod]
    [DataRow("linker help overview", "What Linker can do")]
    [DataRow("linker help navigation", "Navigate and browser actions")]
    [DataRow("linker help tabs", "Tabs")]
    [DataRow("linker help history", "History")]
    [DataRow("linker help collections", "Collections")]
    public async Task SubmitAsync_ReturnsPlainLanguageCapabilityHelp(string prompt, string expectedHeading)
    {
        var response = await CommandCenterChatService.SubmitAsync(prompt);

        Assert.IsFalse(response.IsError);
        StringAssert.Contains(response.Text, expectedHeading);
        Assert.IsFalse(response.Text.Contains("| Tool |", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("active MSN tab", "MSN")]
    [DataRow("activate MSN tab", "MSN")]
    [DataRow("switch MSN", "MSN")]
    [DataRow("switch to MSN tab", "MSN")]
    [DataRow("switch \"Partial TitleName\"", "Partial TitleName")]
    [DataRow("go to my MSN tab", "MSN")]
    public void TryParseBrowserNavigationPrompt_ActivatesTabByPartialTitle(string prompt, string expectedQuery)
    {
        var parsed = CommandCenterChatService.TryParseBrowserNavigationPrompt(prompt, out var command);

        Assert.IsTrue(parsed);
        Assert.AreEqual(BrowserNavigationToolNames.TabsFind, command.ToolName);
        Assert.AreEqual(expectedQuery, command.Arguments["query"]);
    }
}
