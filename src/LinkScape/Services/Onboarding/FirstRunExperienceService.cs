using System.IO;

namespace LinkScape.Services.Onboarding;

internal static class FirstRunExperienceService
{
    internal const string SettingKey = "onboarding.firstRunVersion";
    internal const string ExperienceVersion = "1.0.18";
    internal const string PendingValue = "pending:1.0.18";
    internal static readonly Version MinimumPackageVersion = new(1, 0, 18, 0);

    internal static bool HasExistingSettingsProfile()
    {
        var cacheDatabasePath = Path.Combine(LinkScapeCachePaths.CacheDirectory, "settings.db");
        if (File.Exists(cacheDatabasePath))
        {
            return true;
        }

        try
        {
            return File.Exists(Path.GetFullPath("settings.db"));
        }
        catch
        {
            return false;
        }
    }

    internal static void Initialize(bool hadExistingSettingsProfile) =>
        Initialize(hadExistingSettingsProfile, GetCurrentPackageVersion());

    internal static void Initialize(bool hadExistingSettingsProfile, Version packageVersion)
    {
        if (packageVersion < MinimumPackageVersion ||
            !string.IsNullOrWhiteSpace(SettingsService.GetValue(SettingKey)))
        {
            return;
        }

        SettingsService.SetValue(
            SettingKey,
            hadExistingSettingsProfile ? ExperienceVersion : PendingValue);
    }

    internal static bool ShouldShow() =>
        string.Equals(
            SettingsService.GetValue(SettingKey),
            PendingValue,
            StringComparison.OrdinalIgnoreCase);

    internal static void Complete() =>
        SettingsService.SetValue(SettingKey, ExperienceVersion);

    internal static void ResetForReplay() =>
        SettingsService.SetValue(SettingKey, PendingValue);

    private static Version GetCurrentPackageVersion()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(version.Major, version.Minor, version.Build, version.Revision);
        }
        catch
        {
            // Keep unpackaged development runs testable without affecting the
            // migration decision used by packaged Store builds.
            return MinimumPackageVersion;
        }
    }
}
