using System.Text;
using LinkScape.Models;

namespace LinkScape.Tests;

[TestClass]
public sealed class ActiveTabsPackageTests
{
    [TestMethod]
    public void TryParse_AcceptsActiveTabsContract()
    {
        const string json = """
            {
              "version": 1,
              "source": "test-app",
              "mode": "replace",
              "selectedIndex": 1,
              "saveState": true,
              "collectionName": "Research",
              "tabs": [
                {
                  "id": "docs",
                  "title": "Docs",
                  "url": "https://example.com/docs",
                  "order": 0,
                  "visitedCount": 3,
                  "scrollX": 0,
                  "scrollY": 120
                },
                {
                  "id": "api",
                  "title": "API",
                  "url": "https://example.com/api",
                  "selected": true,
                  "order": 1,
                  "isFavorite": true
                }
              ]
            }
            """;

        var parsed = ActiveTabsPackage.TryParse(json, out var package, out var error);

        Assert.IsTrue(parsed, error);
        Assert.AreEqual("test-app", package.Source);
        Assert.AreEqual("replace", package.Mode);
        Assert.IsTrue(package.ShouldSaveState);
        Assert.AreEqual("Research", package.CollectionName);
        Assert.AreEqual(2, package.ValidTabs.Count);
        Assert.AreEqual("https://example.com/api", package.ValidTabs[1].Url);
    }

    [TestMethod]
    public void TryDecodePayload_AcceptsBase64UrlJson()
    {
        const string json = """{"tabs":[{"url":"https://example.com"}]}""";
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var decoded = ActiveTabsPackage.TryDecodePayload(payload, out var decodedJson);

        Assert.IsTrue(decoded);
        Assert.AreEqual(json, decodedJson);
    }
}
