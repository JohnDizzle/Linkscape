using LinkScape.Services.Application;

namespace LinkScape.Tests;

[TestClass]
public sealed class AppUpdateServiceTests
{
    [TestMethod]
    public void GetWhatsNewPageUrl_AppendsEscapedVersion()
    {
        var url = AppUpdateService.GetWhatsNewPageUrl(" 1.2 beta ");

        Assert.AreEqual(
            "https://linker.local/Updates/index.html?version=1.2%20beta",
            url);
    }
}
