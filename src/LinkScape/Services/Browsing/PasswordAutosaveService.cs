using Microsoft.Web.WebView2.Core;

namespace LinkScape.Services.Browsing;

/// <summary>
/// Owns LinkScape's opt-in WebView2 password-saving preference.
/// WebView2 encrypts saved credentials in its profile; disabling this setting
/// prevents new saves and Save/Update prompts but does not delete existing data.
/// </summary>
internal static class PasswordAutosaveService
{
    internal const string SettingKey = "browser.passwords.autosaveEnabled";

    internal static bool IsEnabled() =>
        bool.TryParse(SettingsService.GetValue(SettingKey), out var enabled) && enabled;

    internal static void Apply(CoreWebView2Profile? profile, bool? enabled = null)
    {
        if (profile is null)
        {
            return;
        }

        try
        {
            profile.IsPasswordAutosaveEnabled = enabled ?? IsEnabled();
        }
        catch (Exception)
        {
            // Older or shutting-down WebView2 runtimes may reject a profile update.
            // Keep browsing available; the preference is applied on the next profile initialization.
        }
    }
}
