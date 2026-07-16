namespace LinkScape;

internal sealed record BrowserNotice(string Message, string Severity = "warning");

internal static class BrowserNoticeService
{
    private static readonly object SyncRoot = new();
    private static BrowserNotice? _currentNotice;

    internal static event Action? NoticeChanged;

    internal static BrowserNotice? CurrentNotice
    {
        get
        {
            lock (SyncRoot)
            {
                return _currentNotice;
            }
        }
    }

    internal static void Show(string message, string severity = "warning")
    {
        lock (SyncRoot)
        {
            _currentNotice = new BrowserNotice(message, severity);
        }

        NoticeChanged?.Invoke();
    }

    internal static void Clear()
    {
        lock (SyncRoot)
        {
            _currentNotice = null;
        }

        NoticeChanged?.Invoke();
    }
}
