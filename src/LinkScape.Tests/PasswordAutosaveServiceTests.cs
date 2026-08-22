namespace LinkScape.Tests;

[TestClass]
public sealed class PasswordAutosaveServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        SettingsService.EnsureDatabase();
    }

    [TestMethod]
    public void IsEnabled_DefaultsOffAndReadsSavedPreference()
    {
        Assert.IsFalse(PasswordAutosaveService.IsEnabled());

        SettingsService.SetValue(PasswordAutosaveService.SettingKey, "true");

        Assert.IsTrue(PasswordAutosaveService.IsEnabled());
    }
}
