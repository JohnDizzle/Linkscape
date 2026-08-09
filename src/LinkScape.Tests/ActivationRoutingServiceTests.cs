using LinkScape;

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
