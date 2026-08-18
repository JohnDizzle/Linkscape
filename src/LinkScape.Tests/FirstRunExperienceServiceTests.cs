namespace LinkScape.Tests;

[TestClass]
public sealed class FirstRunExperienceServiceTests
{
    [TestInitialize]
    public void Initialize()
    {
        TestCacheScope.Reset();
        SettingsService.EnsureDatabase();
        SettingsService.RemoveValue(FirstRunExperienceService.SettingKey);
    }

    [TestMethod]
    public void Initialize_FreshProfileAtVersion118_MarksExperiencePending()
    {
        FirstRunExperienceService.Initialize(
            hadExistingSettingsProfile: false,
            new Version(1, 0, 18, 0));

        Assert.AreEqual(
            FirstRunExperienceService.PendingValue,
            SettingsService.GetValue(FirstRunExperienceService.SettingKey));
        Assert.IsTrue(FirstRunExperienceService.ShouldShow());
    }

    [TestMethod]
    public void Initialize_ExistingProfileAtVersion118_SilentlyCompletesExperience()
    {
        FirstRunExperienceService.Initialize(
            hadExistingSettingsProfile: true,
            new Version(1, 0, 18, 0));

        Assert.AreEqual(
            FirstRunExperienceService.ExperienceVersion,
            SettingsService.GetValue(FirstRunExperienceService.SettingKey));
        Assert.IsFalse(FirstRunExperienceService.ShouldShow());
    }

    [TestMethod]
    public void Initialize_BeforeVersion118_DoesNotCreateSetting()
    {
        FirstRunExperienceService.Initialize(
            hadExistingSettingsProfile: false,
            new Version(1, 0, 17, 9));

        Assert.IsNull(SettingsService.GetValue(FirstRunExperienceService.SettingKey));
    }

    [TestMethod]
    public void Initialize_ExistingState_DoesNotOverwritePendingExperience()
    {
        SettingsService.SetValue(
            FirstRunExperienceService.SettingKey,
            FirstRunExperienceService.PendingValue);

        FirstRunExperienceService.Initialize(
            hadExistingSettingsProfile: true,
            new Version(1, 0, 18, 0));

        Assert.IsTrue(FirstRunExperienceService.ShouldShow());
    }

    [TestMethod]
    public void Complete_StopsExperienceFromShowing()
    {
        FirstRunExperienceService.ResetForReplay();

        FirstRunExperienceService.Complete();

        Assert.AreEqual(
            FirstRunExperienceService.ExperienceVersion,
            SettingsService.GetValue(FirstRunExperienceService.SettingKey));
        Assert.IsFalse(FirstRunExperienceService.ShouldShow());
    }
}
