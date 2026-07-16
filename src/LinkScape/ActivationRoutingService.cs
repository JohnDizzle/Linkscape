using Microsoft.Windows.AppLifecycle;
using System.Net;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace LinkScape;

internal static class ActivationRoutingService
{
    private const string LinkScapeSchemePrefix = "link2scape://";
    private const string LinkScapeSchemePrefixAlt = "link2scape:";
    private static readonly object SyncRoot = new();
    private static string? _pendingTarget;
    private static bool _initialized;

    internal static event Action? ActivationRequested;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var appInstance = AppInstance.GetCurrent();
        TryStorePendingTarget(appInstance.GetActivatedEventArgs());
        appInstance.Activated += OnAppActivated;
    }

    internal static bool TryConsumePendingTarget(out string target)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(_pendingTarget))
            {
                target = string.Empty;
                return false;
            }

            target = _pendingTarget;
            _pendingTarget = null;
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

        lock (SyncRoot)
        {
            _pendingTarget = target;
        }

        return true;
    }

    private static bool TryGetActivationTarget(AppActivationArguments? args, out string target)
    {
        target = string.Empty;

        if (args is null)
        {
            return false;
        }

        return args.Kind switch
        {
            ExtendedActivationKind.Protocol => TryGetProtocolTarget(args.Data as IProtocolActivatedEventArgs, out target),
            ExtendedActivationKind.File => TryGetFileTarget(args.Data as IFileActivatedEventArgs, out target),
            _ => false
        };
    }

    private static bool TryGetProtocolTarget(IProtocolActivatedEventArgs? protocolArgs, out string target)
    {
        target = string.Empty;

        if (protocolArgs?.Uri is not Uri uri)
        {
            return false;
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

        target = candidate;
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

    private static bool TryGetFileTarget(IFileActivatedEventArgs? fileArgs, out string target)
    {
        target = string.Empty;

        var pdfFile = fileArgs?.Files
            .OfType<StorageFile>()
            .FirstOrDefault(file => string.Equals(file.FileType, ".pdf", StringComparison.OrdinalIgnoreCase));

        if (pdfFile is null || string.IsNullOrWhiteSpace(pdfFile.Path))
        {
            return false;
        }

        target = new Uri(pdfFile.Path).AbsoluteUri;
        return true;
    }
}
