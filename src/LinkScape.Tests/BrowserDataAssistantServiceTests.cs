[TestClass]
public sealed class BrowserDataAssistantServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        HistoryPersistenceService.EnsureDatabase();
    }

    [TestMethod]
    public void BuildHistorySearchReport_CleansSearchHistoryForPrompt()
    {
        var result = BrowserDataAssistantService.BuildHistorySearchReport("search history for github");

        StringAssert.Contains(result.Markdown, "contains 'github'");
        Assert.IsFalse(result.Markdown.Contains("contains 'for github'", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void BuildHistoryPeriodReport_UsesQuotedSearchGroupForMonthReport()
    {
        HistoryPersistenceService.UpsertImportedHistory(
        [
            new HistoryItem(
                "https://github.com/JohnDizzle",
                "GitHub Repo",
                new DateTime(2026, 7, 1, 8, 0, 0),
                new DateTime(2026, 7, 2, 8, 0, 0),
                2),
            new HistoryItem(
                "https://example.com/other",
                "Other",
                new DateTime(2026, 7, 1, 8, 0, 0),
                new DateTime(2026, 7, 3, 8, 0, 0),
                1)
        ]);

        var result = BrowserDataAssistantService.BuildHistoryPeriodReport("give a history report about \"Microsoft, azure, and GitHub\" for July 2026");

        StringAssert.Contains(result.Markdown, "## History for July 2026");
        StringAssert.Contains(result.Markdown, "Search group: **Microsoft, azure, GitHub**");
        StringAssert.Contains(result.Markdown, "Pages considered: **1**");
    }

    [TestMethod]
    public void BuildHistoryPeriodReport_ExtractsSearchGroupBeforeThisYear()
    {
        HistoryPersistenceService.UpsertImportedHistory(
        [
            new HistoryItem(
                "https://discord.com/channels",
                "Discord",
                new DateTime(2026, 1, 1, 8, 0, 0),
                new DateTime(2026, 7, 2, 8, 0, 0),
                2),
            new HistoryItem(
                "https://github.com",
                "GitHub",
                new DateTime(2026, 1, 1, 8, 0, 0),
                new DateTime(2026, 7, 3, 8, 0, 0),
                1)
        ]);

        var result = BrowserDataAssistantService.BuildHistoryPeriodReport("history report for discord this year");

        StringAssert.Contains(result.Markdown, "## History for 2026");
        StringAssert.Contains(result.Markdown, "Search group: **discord**");
        StringAssert.Contains(result.Markdown, "Pages considered: **1**");
    }

    [TestMethod]
    public void BuildHistoryPeriodReport_ExtractsDomainSearchGroupBeforeThisYear()
    {
        HistoryPersistenceService.UpsertImportedHistory(
        [
            new HistoryItem(
                "https://discord.com/channels",
                "Discord",
                new DateTime(2026, 1, 1, 8, 0, 0),
                new DateTime(2026, 7, 2, 8, 0, 0),
                2)
        ]);

        var result = BrowserDataAssistantService.BuildHistoryPeriodReport("history report for discord.com this year");

        StringAssert.Contains(result.Markdown, "Search group: **discord.com**");
        StringAssert.Contains(result.Markdown, "Pages considered: **1**");
    }
}
