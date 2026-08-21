using LinkScape;

namespace LinkScape.Tests;

[TestClass]
public sealed class ActivationRoutingServiceTests
{
    [DataTestMethod]
    [DataRow("link2scape://navigate/collections", "Collections")]
    [DataRow("link2scape://navigate/saved-tabs", "SavedTabs")]
    [DataRow("link2scape://navigate/search", "Search")]
    [DataRow("link2scape://navigate/basic", "Search")]
    [DataRow("link2scape://navigate/default", "Search")]
    public void TryMapProtocolUri_MapsNavigationTargets(string rawUri, string expectedKind)
    {
        var mapped = ActivationRoutingService.TryMapProtocolUri(new Uri(rawUri), out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(expectedKind, target.Kind.ToString());
        Assert.AreEqual(string.Empty, target.Value);
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsInstalledAppId()
    {
        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri("link2scape://app/app%20id"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.InstalledApp, target.Kind);
        Assert.AreEqual("app id", target.Value);
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsCollectionId()
    {
        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri("link2scape://collection/personal%20id"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Collection, target.Kind);
        Assert.AreEqual("personal id", target.Value);
        Assert.IsFalse(target.ShouldAppend);
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsAppendCollectionShortcut()
    {
        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri("link2scape://collection/work%20tools?mode=append"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Collection, target.Kind);
        Assert.AreEqual("work tools", target.Value);
        Assert.IsTrue(target.ShouldAppend);
        Assert.IsFalse(target.ShouldStop);
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsStopCollectionCommand()
    {
        var uri = ActivationRoutingService.CreateCollectionActivationUri("work tools", stop: true);

        Assert.AreEqual("link2scape://collection/work%20tools?mode=stop", uri);
        Assert.IsTrue(ActivationRoutingService.TryMapProtocolUri(new Uri(uri), out var target));
        Assert.IsFalse(target.ShouldAppend);
        Assert.IsTrue(target.ShouldStop);
    }

    [TestMethod]
    public void CreateCollectionActivationUri_UsesShortAppendRoute()
    {
        var uri = ActivationRoutingService.CreateCollectionActivationUri("work tools", append: true);

        Assert.AreEqual("link2scape://collection/work%20tools?mode=append", uri);
        Assert.IsTrue(ActivationRoutingService.TryMapProtocolUri(new Uri(uri), out var target));
        Assert.AreEqual("work tools", target.Value);
        Assert.IsTrue(target.ShouldAppend);
        Assert.AreEqual(uri, CollectionShortcutService.CreateActivationUri("work tools"));
    }

    [DataTestMethod]
    [DataRow("link2scape://collection/work?mode=replace")]
    [DataRow("link2scape://collection/work?mode=appendix")]
    [DataRow("link2scape://collection/work?other=append")]
    public void TryMapProtocolUri_DoesNotAppendForUnrelatedQueryValues(string rawUri)
    {
        Assert.IsTrue(ActivationRoutingService.TryMapProtocolUri(new Uri(rawUri), out var target));
        Assert.IsFalse(target.ShouldAppend);
    }

    [TestMethod]
    public void TryMapProtocolUri_PreservesExistingOpenUrlRoute()
    {
        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri("link2scape://open?url=https%3A%2F%2Fexample.com%2Fdocs"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Url, target.Kind);
        Assert.AreEqual("https://example.com/docs", target.Value);
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsOpenTabsJsonPackage()
    {
        const string json = """{"tabs":[{"url":"https://example.com/docs","title":"Docs","selected":true}]}""";
        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri($"link2scape://open?tabs={Uri.EscapeDataString(json)}"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.ActiveTabsPackage, target.Kind);
        StringAssert.Contains(target.Value, "https://example.com/docs");
    }

    [TestMethod]
    public void TryMapProtocolUri_MapsTabsActiveBase64UrlPayload()
    {
        const string json = """{"mode":"replace","saveState":false,"tabs":[{"url":"https://example.com/api"}]}""";
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var mapped = ActivationRoutingService.TryMapProtocolUri(
            new Uri($"link2scape://tabs/active?payload={payload}"),
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.ActiveTabsPackage, target.Kind);
        StringAssert.Contains(target.Value, "\"saveState\":false");
    }

    [TestMethod]
    public void TryMapLaunchArguments_MapsPlainLaunchToMainBrowser()
    {
        var mapped = ActivationRoutingService.TryMapLaunchArguments(string.Empty, out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.MainBrowser, target.Kind);
    }

    [TestMethod]
    public void TryMapLaunchArguments_MapsJumpListAppLaunch()
    {
        var mapped = ActivationRoutingService.TryMapLaunchArguments(
            "link2scape://app/app%20id",
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.InstalledApp, target.Kind);
        Assert.AreEqual("app id", target.Value);
    }

    [TestMethod]
    public void TryGetCommandLineTarget_MapsCollectionToFreshWindowActivation()
    {
        var mapped = ActivationRoutingService.TryGetCommandLineTarget(
            ["LinkScape.exe", "--linkscape-new-window-target", "link2scape://collection/work%20items"],
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Collection, target.Kind);
        Assert.AreEqual("work items", target.Value);
    }

    [TestMethod]
    public void TryMapNewWindowLaunchArguments_MapsJumpListSearchActivation()
    {
        var mapped = ActivationRoutingService.TryMapNewWindowLaunchArguments(
            "--linkscape-new-window-target link2scape://navigate/search",
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Search, target.Kind);
    }

    [TestMethod]
    public void TryMapNewWindowLaunchArguments_MapsJumpListCollectionActivation()
    {
        var mapped = ActivationRoutingService.TryMapNewWindowLaunchArguments(
            "--linkscape-new-window-target link2scape://collection/personal%20items",
            out var target);

        Assert.IsTrue(mapped);
        Assert.AreEqual(ActivationTargetKind.Collection, target.Kind);
        Assert.AreEqual("personal items", target.Value);
    }
}
