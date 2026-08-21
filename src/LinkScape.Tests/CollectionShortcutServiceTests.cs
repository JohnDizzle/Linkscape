namespace LinkScape.Tests;

[TestClass]
public sealed class CollectionShortcutServiceTests
{
    [TestMethod]
    public void WriteShortcutFile_RoundTripsActivationAndIcon()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"LinkScapeShortcut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var shortcutPath = Path.Combine(temporaryDirectory, "Start Work - LinkScape.url");
        var iconPath = Path.Combine(temporaryDirectory, "LinkScape.ico");
        const string activationUri = "link2scape://collection/work?mode=append";

        try
        {
            File.WriteAllBytes(iconPath, [0]);
            CollectionShortcutService.WriteShortcutFile(shortcutPath, activationUri, iconPath);

            Assert.IsTrue(File.Exists(shortcutPath));
            Assert.IsTrue(CollectionShortcutService.TryReadShortcut(shortcutPath, out var actualUri, out var actualIcon));
            Assert.AreEqual(activationUri, actualUri);
            Assert.AreEqual(iconPath, actualIcon);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
