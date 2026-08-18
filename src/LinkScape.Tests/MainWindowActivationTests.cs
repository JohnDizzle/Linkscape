using LinkScape.Application;
using Windows.Graphics;

namespace LinkScape.Tests;

[TestClass]
public sealed class MainWindowActivationTests
{
    [TestMethod]
    public void CalculateFirstLaunchBoundsCentersPreferredWidthAndUsesWorkAreaHeight()
    {
        var result = MainWindowActivation.CalculateFirstLaunchBounds(
            new RectInt32(0, 0, 1920, 1040),
            1200);

        Assert.AreEqual(new RectInt32(360, 0, 1200, 1040), result);
    }

    [TestMethod]
    public void CalculateFirstLaunchBoundsFitsInsideLaptopWorkArea()
    {
        var result = MainWindowActivation.CalculateFirstLaunchBounds(
            new RectInt32(0, 0, 1366, 728),
            1200);

        Assert.AreEqual(new RectInt32(83, 0, 1200, 728), result);
    }

    [TestMethod]
    public void CalculateFirstLaunchBoundsHonorsWorkAreaOffset()
    {
        var result = MainWindowActivation.CalculateFirstLaunchBounds(
            new RectInt32(1920, 40, 1600, 900),
            1200);

        Assert.AreEqual(new RectInt32(2120, 40, 1200, 900), result);
    }
}
