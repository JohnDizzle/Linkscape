namespace LinkScape;

internal static class AppErrorStateService
{
    private static readonly object SyncRoot = new();
    private static Exception? _currentError;

    internal static event Action? ErrorChanged;

    internal static Exception? CurrentError
    {
        get
        {
            lock (SyncRoot)
            {
                return _currentError;
            }
        }
    }

    internal static void SetError(Exception exception)
    {
        lock (SyncRoot)
        {
            _currentError = exception;
        }

        ErrorChanged?.Invoke();
    }

    internal static void Clear()
    {
        lock (SyncRoot)
        {
            _currentError = null;
        }

        ErrorChanged?.Invoke();
    }
}
