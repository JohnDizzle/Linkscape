using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using LinkScape.Models;

namespace LinkScape.Services.Application;

internal static class ActivationRoutingService
{
    private const string MainInstanceKey = "main";
    private const string LinkScapeSchemePrefix = "link2scape://";
    private const string LinkScapeSchemePrefixAlt = "link2scape:";
    private const string NewWindowUrlArgument = "--linkscape-new-window-url";
    private const string NewWindowTargetArgument = "--linkscape-new-window-target";
    private static readonly object SyncRoot = new();
    private static ActivationTarget? _pendingTarget;
    private static bool _pendingTargetIsFreshWindow;
    private static bool _initialized;

    internal static event Action? ActivationRequested;

    internal static async Task<bool> InitializeAsync()
    {
        if (_initialized)
        {
            return true;
        }

        var appInstance = AppInstance.GetCurrent();
        var activatedArgs = appInstance.GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        var hasCommandLineTarget = TryGetCommandLineTarget(Environment.GetCommandLineArgs(), out var commandLineTarget);
        var hasNewWindowActivationTarget = TryGetNewWindowActivationTarget(activatedArgs, out var newWindowActivationTarget);
        var hasActivationTarget = TryGetActivationTarget(activatedArgs, out _);

        if (!mainInstance.IsCurrent && hasNewWindowActivationTarget)
        {
            _initialized = true;
            StorePendingTarget(newWindowActivationTarget, isFreshWindow: true);
            return true;
        }

        if (!mainInstance.IsCurrent && hasActivationTarget && !hasCommandLineTarget)
        {
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            return false;
        }

        if (!mainInstance.IsCurrent && hasCommandLineTarget)
        {
            _initialized = true;
            StorePendingTarget(commandLineTarget, isFreshWindow: true);
            return true;
        }

        if (!mainInstance.IsCurrent)
        {
            return true;
        }

        _initialized = true;
        if (hasCommandLineTarget)
        {
            StorePendingTarget(commandLineTarget, isFreshWindow: true);
        }
        else if (hasNewWindowActivationTarget)
        {
            StorePendingTarget(newWindowActivationTarget, isFreshWindow: true);
        }
        else
        {
            TryStorePendingTarget(activatedArgs);
        }

        mainInstance.Activated += OnAppActivated;
        return true;
    }

    internal static bool OpenUrlInNewWindow(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return false;
        }

        return OpenInNewWindow(NewWindowUrlArgument, url);
    }

    internal static bool OpenCollectionInNewWindow(string collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return false;
        }

        var target = $"link2scape://collection/{Uri.EscapeDataString(collectionId)}";
        return OpenInNewWindow(NewWindowTargetArgument, target);
    }

    internal static string CreateNewWindowActivationArguments(string protocolUri) =>
        $"{NewWindowTargetArgument} {protocolUri}";

    internal static bool RequestCollectionActivation(string collectionId, bool append = false, bool stop = false)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return false;
        }

        var uri = new Uri(CreateCollectionActivationUri(collectionId, append, stop));
        if (!TryMapProtocolUri(uri, out var target) || ActivationRequested is not { } activationRequested)
        {
            return false;
        }

        StorePendingTarget(target, isFreshWindow: false);
        activationRequested.Invoke();
        return true;
    }

    internal static string CreateCollectionActivationUri(string collectionId, bool append = false, bool stop = false)
    {
        var uri = $"link2scape://collection/{Uri.EscapeDataString(collectionId)}";
        return stop ? $"{uri}?mode=stop" : append ? $"{uri}?mode=append" : uri;
    }

    private static bool OpenInNewWindow(string argumentName, string target)
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add(argumentName);
            startInfo.ArgumentList.Add(target);
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryConsumePendingTarget(out ActivationTarget target)
    {
        return TryConsumePendingTarget(out target, out _);
    }

    internal static bool TryConsumePendingTarget(out ActivationTarget target, out bool isFreshWindow)
    {
        lock (SyncRoot)
        {
            if (_pendingTarget is null)
            {
                target = default!;
                isFreshWindow = false;
                return false;
            }

            target = _pendingTarget;
            isFreshWindow = _pendingTargetIsFreshWindow;
            _pendingTarget = null;
            _pendingTargetIsFreshWindow = false;
            return true;
        }
    }

    private static void OnAppActivated(object? sender, AppActivationArguments args)
    {
        if (!TryStorePendingTarget(args))
        {
            return;
        }

        ActivationRequested?.Invoke();
    }

    private static bool TryStorePendingTarget(AppActivationArguments? args)
    {
        if (!TryGetActivationTarget(args, out var target))
        {
            return false;
        }

        StorePendingTarget(target, isFreshWindow: false);
        return true;
    }

    private static void StorePendingTarget(ActivationTarget target, bool isFreshWindow)
    {
        lock (SyncRoot)
        {
            _pendingTarget = target;
            _pendingTargetIsFreshWindow = isFreshWindow;
        }
    }

    private static bool TryGetActivationTarget(AppActivationArguments? args, out ActivationTarget target)
    {
        target = default!;

        if (args is null)
        {
            return false;
        }

        return args.Kind switch
        {
            ExtendedActivationKind.Protocol => TryGetProtocolTarget(args.Data as IProtocolActivatedEventArgs, out target),
            ExtendedActivationKind.File => TryGetFileTarget(args.Data as IFileActivatedEventArgs, out target),
            ExtendedActivationKind.ShareTarget => TryGetShareTarget(args.Data as IShareTargetActivatedEventArgs, out target),
            ExtendedActivationKind.Launch => TryGetLaunchTarget(args.Data as ILaunchActivatedEventArgs, out target),
            _ => false
        };
    }

    internal static bool TryGetCommandLineTarget(string[] args, out ActivationTarget target)
    {
        target = default!;

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], NewWindowTargetArgument, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate(args[index + 1].Trim(), UriKind.Absolute, out var targetUri) &&
                    TryMapProtocolUri(targetUri, out target);
            }

            if (!string.Equals(args[index], NewWindowUrlArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = args[index + 1].Trim();

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                return false;
            }

            target = ActivationTarget.ForUrl(candidate);
            return true;
        }

        return false;
    }

    private static bool TryGetProtocolTarget(IProtocolActivatedEventArgs? protocolArgs, out ActivationTarget target)
    {
        target = default!;

        if (protocolArgs?.Uri is not Uri uri)
        {
            return false;
        }

        return TryMapProtocolUri(uri, out target);
    }

    private static bool TryGetLaunchTarget(ILaunchActivatedEventArgs? launchArgs, out ActivationTarget target)
    {
        return TryMapLaunchArguments(launchArgs?.Arguments, out target);
    }

    internal static bool TryMapLaunchArguments(string? rawArguments, out ActivationTarget target)
    {
        var arguments = rawArguments?.Trim();

        if (string.IsNullOrWhiteSpace(arguments))
        {
            target = new ActivationTarget(ActivationTargetKind.MainBrowser);
            return true;
        }

        if (TryMapNewWindowLaunchArguments(arguments, out target))
        {
            return true;
        }

        target = default!;
        return Uri.TryCreate(arguments, UriKind.Absolute, out var uri) &&
            TryMapProtocolUri(uri, out target);
    }

    internal static bool TryMapNewWindowLaunchArguments(string? rawArguments, out ActivationTarget target)
    {
        target = default!;
        var arguments = rawArguments?.Trim();

        if (string.IsNullOrWhiteSpace(arguments) ||
            !arguments.StartsWith(NewWindowTargetArgument, StringComparison.OrdinalIgnoreCase) ||
            (arguments.Length > NewWindowTargetArgument.Length &&
             !char.IsWhiteSpace(arguments[NewWindowTargetArgument.Length])))
        {
            return false;
        }

        var payload = arguments[NewWindowTargetArgument.Length..].Trim().Trim('"');
        return Uri.TryCreate(payload, UriKind.Absolute, out var uri) &&
            TryMapProtocolUri(uri, out target);
    }

    private static bool TryGetNewWindowActivationTarget(
        AppActivationArguments? args,
        out ActivationTarget target)
    {
        target = default!;
        return args?.Kind == ExtendedActivationKind.Launch &&
            TryMapNewWindowLaunchArguments(
                (args.Data as ILaunchActivatedEventArgs)?.Arguments,
                out target);
    }

    internal static bool TryMapProtocolUri(Uri uri, out ActivationTarget target)
    {
        target = default!;

        if (!string.Equals(uri.Scheme, "link2scape", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var route = uri.Host.Trim();
        var path = WebUtility.UrlDecode(uri.AbsolutePath.Trim('/'));

        if (string.Equals(route, "app", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(path))
        {
            target = new ActivationTarget(ActivationTargetKind.InstalledApp, path);
            return true;
        }

        if (string.Equals(route, "collection", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(path))
        {
            target = new ActivationTarget(
                ActivationTargetKind.Collection,
                path,
                ShouldAppend: HasQueryValue(uri.Query, "mode", "append"),
                ShouldStop: HasQueryValue(uri.Query, "mode", "stop"));
            return true;
        }

        if (string.Equals(route, "tabs", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path, "active", StringComparison.OrdinalIgnoreCase) &&
            TryExtractTabsPackageJson(uri.Query, out var tabsPackageJson))
        {
            target = new ActivationTarget(ActivationTargetKind.ActiveTabsPackage, tabsPackageJson);
            return true;
        }

        if (string.Equals(route, "navigate", StringComparison.OrdinalIgnoreCase))
        {
            var kind = path.ToLowerInvariant() switch
            {
                "collections" => ActivationTargetKind.Collections,
                "saved-tabs" => ActivationTargetKind.SavedTabs,
                "search" or "basic" or "default" => ActivationTargetKind.Search,
                _ => ActivationTargetKind.None
            };

            if (kind != ActivationTargetKind.None)
            {
                target = new ActivationTarget(kind);
                return true;
            }
        }

        var raw = uri.OriginalString;
        string payload;

        if (raw.StartsWith(LinkScapeSchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            payload = raw[LinkScapeSchemePrefix.Length..];
        }
        else if (raw.StartsWith(LinkScapeSchemePrefixAlt, StringComparison.OrdinalIgnoreCase))
        {
            payload = raw[LinkScapeSchemePrefixAlt.Length..];
        }
        else
        {
            return false;
        }

        if (TryExtractTabsPackageJson(uri.Query, out tabsPackageJson))
        {
            target = new ActivationTarget(ActivationTargetKind.ActiveTabsPackage, tabsPackageJson);
            return true;
        }

        var queryTarget = TryExtractQueryUrl(payload);
        var candidate = string.IsNullOrWhiteSpace(queryTarget)
            ? WebUtility.UrlDecode(payload)
            : queryTarget;

        candidate = candidate.Trim();

        if (candidate.StartsWith("//", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            return false;
        }

        target = ActivationTarget.ForUrl(candidate);
        return true;
    }

    private static string? TryExtractQueryUrl(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var query = payload.TrimStart('/');

        if (query.StartsWith("open?", StringComparison.OrdinalIgnoreCase))
        {
            query = query[5..];
        }
        else if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }
        else
        {
            return null;
        }

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);

            if (parts.Length == 2 && string.Equals(parts[0], "url", StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(parts[1]);
            }
        }

        return null;
    }

    private static bool TryExtractTabsPackageJson(string query, out string json)
    {
        json = string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmedQuery = query.TrimStart('?');
        foreach (var segment in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length != 2 ||
                (!string.Equals(parts[0], "tabs", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parts[0], "payload", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (ActiveTabsPackage.TryDecodePayload(parts[1], out json) &&
                ActiveTabsPackage.TryParse(json, out _, out _))
            {
                return true;
            }
        }

        json = string.Empty;
        return false;
    }

    private static bool HasQueryValue(string query, string key, string expectedValue)
    {
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 &&
                string.Equals(WebUtility.UrlDecode(parts[0]), key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(WebUtility.UrlDecode(parts[1]), expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFileTarget(IFileActivatedEventArgs? fileArgs, out ActivationTarget target)
    {
        target = default!;

        var pdfFile = fileArgs?.Files
            .OfType<StorageFile>()
            .FirstOrDefault(file => string.Equals(file.FileType, ".pdf", StringComparison.OrdinalIgnoreCase));

        if (pdfFile is null || string.IsNullOrWhiteSpace(pdfFile.Path))
        {
            return false;
        }

        target = ActivationTarget.ForUrl(new Uri(pdfFile.Path).AbsoluteUri);
        return true;
    }

    private static bool TryGetShareTarget(IShareTargetActivatedEventArgs? shareArgs, out ActivationTarget target)
    {
        if (shareArgs?.ShareOperation is not { } shareOperation)
        {
            target = default!;
            return false;
        }

        target = ActivationTarget.ForShare(shareOperation);
        return true;
    }
}

internal enum ActivationTargetKind
{
    None,
    Url,
    InstalledApp,
    Collection,
    ActiveTabsPackage,
    Collections,
    SavedTabs,
    Search,
    ShareTarget,
    MainBrowser
}

internal sealed record ActivationTarget(
    ActivationTargetKind Kind,
    string Value = "",
    ShareOperation? ShareOperation = null,
    bool ShouldAppend = false,
    bool ShouldStop = false)
{
    internal static ActivationTarget ForUrl(string url) => new(ActivationTargetKind.Url, url);

    internal static ActivationTarget ForShare(ShareOperation shareOperation) =>
        new(ActivationTargetKind.ShareTarget, ShareOperation: shareOperation);
}
