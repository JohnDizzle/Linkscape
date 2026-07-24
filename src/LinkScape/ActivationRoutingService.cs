using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace LinkScape;

internal static class ActivationRoutingService
{
    private const string MainInstanceKey = "main";
    private const string LinkScapeSchemePrefix = "link2scape://";
    private const string LinkScapeSchemePrefixAlt = "link2scape:";
    private const string NewWindowUrlArgument = "--linkscape-new-window-url";
    private static readonly object SyncRoot = new();
    private static string? _pendingTarget;
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
        var hasActivationTarget = TryGetActivationTarget(activatedArgs, out _);

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

            startInfo.ArgumentList.Add(NewWindowUrlArgument);
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryConsumePendingTarget(out string target)
    {
        return TryConsumePendingTarget(out target, out _);
    }

    internal static bool TryConsumePendingTarget(out string target, out bool isFreshWindow)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(_pendingTarget))
            {
                target = string.Empty;
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

    private static void StorePendingTarget(string target, bool isFreshWindow)
    {
        lock (SyncRoot)
        {
            _pendingTarget = target;
            _pendingTargetIsFreshWindow = isFreshWindow;
        }
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

    private static bool TryGetCommandLineTarget(string[] args, out string target)
    {
        target = string.Empty;

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], NewWindowUrlArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = args[index + 1].Trim();

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                return false;
            }

            target = candidate;
            return true;
        }

        return false;
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
