using LinkScape.Models;

namespace LinkScape.Services;

public static class WebAppStateService
{
    public static InstalledWebApp? FindInstalled(InstallableWebApp? candidate)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.ManifestUrl))
        {
            return null;
        }

        return InstalledWebAppService
            .GetAll()
            .FirstOrDefault(app =>
                string.Equals(
                    app.ManifestUrl,
                    candidate.ManifestUrl,
                    StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsInstalled(InstallableWebApp? candidate) =>
        FindInstalled(candidate) is not null;

    public static bool TryOpenInstalled(InstallableWebApp? candidate)
    {
        var installed = FindInstalled(candidate);
        if (installed is null)
        {
            return false;
        }

        WebAppWindowService.Open(installed);
        return true;
    }
}
