[TestClass]
public sealed class AddressSearchServiceTests
{
    [TestMethod]
    public void ParseAiResults_AcceptsJsonCodeFenceAndCapsResults()
    {
        const string json = """
            ```json
            [
              {"title":"OpenAI","url":"https://openai.com","snippet":"AI research"},
              {"title":"Invalid","url":"javascript:alert(1)","snippet":"Ignore"},
              {"title":"Microsoft","url":"https://microsoft.com","snippet":"Software"}
            ]
            ```
            """;

        var results = AddressSearchService.ParseAiResults(json, 5);

        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(result => result.Source == AddressSearchSource.AiResults));
        Assert.AreEqual("https://openai.com", results[0].Url);
    }

    [TestMethod]
    public void ParseAiResults_AcceptsAzureStyleProseAroundJson()
    {
        const string response = """
            Here are several useful results:
            [
              {"title":"Azure","url":"https://azure.microsoft.com","snippet":"Cloud platform"}
            ]
            I hope this helps.
            """;

        var results = AddressSearchService.ParseAiResults(response, 5);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Azure", results[0].Title);
    }

    [TestMethod]
    public void ParseAiResults_FallsBackToMarkdownLinks()
    {
        const string response = "- [Microsoft Learn](https://learn.microsoft.com) — Documentation";

        var results = AddressSearchService.ParseAiResults(response, 5);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("https://learn.microsoft.com", results[0].Url);
    }
}
