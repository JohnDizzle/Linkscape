[TestClass]
public sealed class SettingsServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
    }

    [TestMethod]
    public void EnsureDatabase_CreatesDefaultSetting()
    {
        SettingsService.EnsureDatabase();

        Assert.AreEqual("John M. Doyle", SettingsService.GetValue("Developer"));
    }

    [TestMethod]
    public void GetValue_ReturnsNull_ForBlankOrMissingKey()
    {
        SettingsService.EnsureDatabase();

        Assert.IsNull(SettingsService.GetValue(""));
        Assert.IsNull(SettingsService.GetValue("missing"));
    }

    [TestMethod]
    public void GetValueOrDefault_ReturnsFallback_WhenValueMissing()
    {
        SettingsService.EnsureDatabase();

        var value = SettingsService.GetValueOrDefault("missing", "fallback");

        Assert.AreEqual("fallback", value);
    }

    [TestMethod]
    public void SetValue_PersistsValue_AndRaisesChangedEvent()
    {
        SettingsService.EnsureDatabase();

        string? changedKey = null;
        string? changedValue = null;

        void Handler(string key, string? value)
        {
            changedKey = key;
            changedValue = value;
        }

        SettingsService.SettingChanged += Handler;

        try
        {
            SettingsService.SetValue("HomeUrl", "https://example.com");
        }
        finally
        {
            SettingsService.SettingChanged -= Handler;
        }

        Assert.AreEqual("https://example.com", SettingsService.GetValue("HomeUrl"));
        Assert.AreEqual("HomeUrl", changedKey);
        Assert.AreEqual("https://example.com", changedValue);
    }

    [TestMethod]
    public void RemoveValue_RemovesExistingValue_AndRaisesNullEvent()
    {
        SettingsService.EnsureDatabase();
        SettingsService.SetValue("Theme", "Dark");

        string? changedKey = null;
        string? changedValue = "sentinel";

        void Handler(string key, string? value)
        {
            changedKey = key;
            changedValue = value;
        }

        SettingsService.SettingChanged += Handler;

        bool removed;
        try
        {
            removed = SettingsService.RemoveValue("Theme");
        }
        finally
        {
            SettingsService.SettingChanged -= Handler;
        }

        Assert.IsTrue(removed);
        Assert.IsNull(SettingsService.GetValue("Theme"));
        Assert.AreEqual("Theme", changedKey);
        Assert.IsNull(changedValue);
    }

    [TestMethod]
    public void RemoveValue_ReturnsFalse_ForBlankOrMissingKey()
    {
        SettingsService.EnsureDatabase();

        Assert.IsFalse(SettingsService.RemoveValue(""));
        Assert.IsFalse(SettingsService.RemoveValue("missing"));
    }

    [TestMethod]
    public void Dump_ReturnsAllPersistedValues()
    {
        SettingsService.EnsureDatabase();
        SettingsService.SetValue("Theme", "Dark");
        SettingsService.SetValue("HomeUrl", "https://example.com");

        var values = SettingsService.Dump();

        Assert.AreEqual("John M. Doyle", values["Developer"]);
        Assert.AreEqual("Dark", values["Theme"]);
        Assert.AreEqual("https://example.com", values["HomeUrl"]);
    }
}
