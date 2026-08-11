using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace LinkScape.Services;

internal static class WindowsShareService
{
    private static readonly Guid DataTransferManagerIid =
        new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    private static readonly object SyncRoot = new();
    private static IDataTransferManagerInterop? _interop;
    private static DataTransferManager? _manager;
    private static SharePayload? _pendingPayload;
    private static nint _registeredHwnd;

    internal static async Task SharePageAsync(
        string? title,
        string? url,
        string? imageDataUrl)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Shared from LinkScape" : title.Trim();
        var normalizedUrl = url?.Trim() ?? string.Empty;
        var imageStream = await TryCreateImageStreamAsync(imageDataUrl);
        var text = string.IsNullOrWhiteSpace(normalizedUrl)
            ? normalizedTitle
            : $"{normalizedTitle}{Environment.NewLine}{normalizedUrl}";

        Show(new SharePayload(
            normalizedTitle,
            "Current page shared from LinkScape Browser.",
            text,
            TryCreateWebUri(normalizedUrl),
            imageStream));
    }

    internal static void ShareLinkerMessage(string? message)
    {
        var text = message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            BrowserNoticeService.Show("There is no Linker message to share.");
            return;
        }

        Show(new SharePayload(
            "Linker message",
            "A response shared from LinkScape Browser.",
            text,
            null,
            null));
    }

    private static void Show(SharePayload payload)
    {
        var hwnd = global::MainWindowActivation.Hwnd;
        if (hwnd == 0)
        {
            BrowserNoticeService.Show("The Windows Share panel is not available yet.");
            payload.Dispose();
            return;
        }

        try
        {
            EnsureRegistered(hwnd);

            lock (SyncRoot)
            {
                _pendingPayload?.Dispose();
                _pendingPayload = payload;
            }

            _interop!.ShowShareUIForWindow(hwnd);
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(_pendingPayload, payload))
                {
                    _pendingPayload = null;
                }
            }

            payload.Dispose();
            BrowserNoticeService.Show($"Could not open the Windows Share panel: {ex.Message}");
        }
    }

    private static void EnsureRegistered(nint hwnd)
    {
        if (_manager is not null && _interop is not null && _registeredHwnd == hwnd)
        {
            return;
        }

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var managerPointer = interop.GetForWindow(hwnd, DataTransferManagerIid);
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(managerPointer);
        manager.DataRequested += OnDataRequested;

        _interop = interop;
        _manager = manager;
        _registeredHwnd = hwnd;
    }

    private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        SharePayload? payload;
        lock (SyncRoot)
        {
            payload = _pendingPayload;
        }

        if (payload is null)
        {
            args.Request.FailWithDisplayText("LinkScape does not have anything ready to share.");
            return;
        }

        var package = args.Request.Data;
        package.Properties.Title = payload.Title;
        package.Properties.Description = payload.Description;
        package.RequestedOperation = DataPackageOperation.Copy;
        package.SetText(payload.Text);

        if (payload.WebUri is not null)
        {
            package.SetWebLink(payload.WebUri);
        }

        if (payload.ImageStream is not null)
        {
            var imageReference = RandomAccessStreamReference.CreateFromStream(payload.ImageStream);
            package.SetBitmap(imageReference);
            package.Properties.Thumbnail = imageReference;
        }
    }

    private static Uri? TryCreateWebUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? uri
            : null;

    private static async Task<InMemoryRandomAccessStream?> TryCreateImageStreamAsync(string? imageDataUrl)
    {
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return null;
        }

        var separatorIndex = imageDataUrl.IndexOf(',');
        if (separatorIndex < 0 ||
            !imageDataUrl[..separatorIndex].Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(imageDataUrl[(separatorIndex + 1)..]);
            var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            return stream;
        }
        catch (Exception ex) when (ex is FormatException or COMException)
        {
            LocalMcpDiagnostics.Trace("WindowsShare", $"Image preparation failed: {ex.Message}");
            return null;
        }
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        nint GetForWindow([In] nint appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow(nint appWindow);
    }

    private sealed record SharePayload(
        string Title,
        string Description,
        string Text,
        Uri? WebUri,
        InMemoryRandomAccessStream? ImageStream) : IDisposable
    {
        public void Dispose() => ImageStream?.Dispose();
    }
}
