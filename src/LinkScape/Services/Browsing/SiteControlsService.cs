using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkScape.Services.Browsing;

internal sealed record SitePermissionSetting(
    CoreWebView2PermissionKind Kind,
    string DisplayName,
    CoreWebView2PermissionState State);

internal sealed record SiteControlsSnapshot(
    bool IsAvailable,
    string Origin,
    string Host,
    string ConnectionLabel,
    bool IsSecure,
    int CookieCount,
    long StorageUsageBytes,
    IReadOnlyList<SitePermissionSetting> Permissions,
    string? Error = null);

internal static class SiteControlsService
{
    private static readonly (CoreWebView2PermissionKind Kind, string Name)[] PermissionDefinitions =
    [
        (CoreWebView2PermissionKind.Camera, "Camera"),
        (CoreWebView2PermissionKind.Microphone, "Microphone"),
        (CoreWebView2PermissionKind.Geolocation, "Location")
    ];

    internal static bool TryGetOrigin(string? value, out string origin, out string host)
    {
        origin = string.Empty;
        host = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri.Scheme, uri.IdnHost)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        origin = builder.Uri.GetLeftPart(UriPartial.Authority);
        host = uri.IdnHost;
        return true;
    }

    internal static async Task<SiteControlsSnapshot> GetSnapshotAsync(CoreWebView2? core)
    {
        if (core is null || !TryGetOrigin(core.Source, out var origin, out var host))
        {
            return Unavailable("Site controls are unavailable for this page.");
        }

        try
        {
            var savedSettings = await core.Profile.GetNonDefaultPermissionSettingsAsync();
            var stateByKind = savedSettings
                .Where(setting => OriginsMatch(setting.PermissionOrigin, origin))
                .GroupBy(setting => setting.PermissionKind)
                .ToDictionary(group => group.Key, group => group.Last().PermissionState);

            var permissions = PermissionDefinitions
                .Select(definition => new SitePermissionSetting(
                    definition.Kind,
                    definition.Name,
                    stateByKind.GetValueOrDefault(definition.Kind, CoreWebView2PermissionState.Default)))
                .ToArray();

            var cookies = await core.CookieManager.GetCookiesAsync(origin);
            var storageUsage = await GetStorageUsageAsync(core, origin);
            var isInternal = string.Equals(host, "linker.local", StringComparison.OrdinalIgnoreCase);
            var isSecure = origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || isInternal;

            return new SiteControlsSnapshot(
                true,
                origin,
                host,
                isInternal ? "LinkScape internal page" : isSecure ? "Secure connection (HTTPS)" : "Not secure (HTTP)",
                isSecure,
                cookies.Count,
                storageUsage,
                permissions);
        }
        catch (Exception ex)
        {
            return Unavailable($"Site controls could not be loaded: {ex.Message}", origin, host);
        }
    }

    internal static async Task SetPermissionAsync(
        CoreWebView2? core,
        CoreWebView2PermissionKind kind,
        CoreWebView2PermissionState state)
    {
        if (core is null || !TryGetOrigin(core.Source, out var origin, out _))
        {
            throw new InvalidOperationException("The active page does not have a configurable web origin.");
        }

        await core.Profile.SetPermissionStateAsync(kind, origin, state);
    }

    internal static async Task ResetPermissionsAsync(CoreWebView2? core)
    {
        if (core is null || !TryGetOrigin(core.Source, out var origin, out _))
        {
            throw new InvalidOperationException("The active page does not have a configurable web origin.");
        }

        var savedSettings = await core.Profile.GetNonDefaultPermissionSettingsAsync();
        var resetTasks = savedSettings
            .Where(setting => OriginsMatch(setting.PermissionOrigin, origin))
            .Select(setting => core.Profile.SetPermissionStateAsync(
                setting.PermissionKind,
                origin,
                CoreWebView2PermissionState.Default).AsTask());

        await Task.WhenAll(resetTasks);
    }

    internal static async Task ClearSiteDataAsync(CoreWebView2? core)
    {
        if (core is null || !TryGetOrigin(core.Source, out var origin, out _))
        {
            throw new InvalidOperationException("The active page does not have a clearable web origin.");
        }

        var cookies = await core.CookieManager.GetCookiesAsync(origin);
        foreach (var cookie in cookies)
        {
            core.CookieManager.DeleteCookie(cookie);
        }

        var parameters = JsonSerializer.Serialize(new
        {
            origin,
            storageTypes = "all"
        });

        _ = await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin", parameters);
    }

    internal static string FormatStorageUsage(long bytes)
    {
        if (bytes <= 0)
        {
            return "No stored site data";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]} stored";
    }

    private static async Task<long> GetStorageUsageAsync(CoreWebView2 core, string origin)
    {
        try
        {
            var parameters = JsonSerializer.Serialize(new { origin });
            var response = await core.CallDevToolsProtocolMethodAsync("Storage.getUsageAndQuota", parameters);
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetDouble(out var bytes)
                ? Math.Max(0, (long)bytes)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool OriginsMatch(string? left, string right) =>
        TryGetOrigin(left, out var normalizedLeft, out _) &&
        string.Equals(normalizedLeft, right, StringComparison.OrdinalIgnoreCase);

    private static SiteControlsSnapshot Unavailable(string message, string origin = "", string host = "") =>
        new(false, origin, host, "Unavailable", false, 0, 0, [], message);
}
