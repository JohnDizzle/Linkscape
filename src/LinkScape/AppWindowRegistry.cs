using Microsoft.UI.Xaml;

namespace LinkScape;

/// <summary>
/// Owns secondary top-level windows created by LinkScape features such as installed web apps.
/// Keeping strong references here prevents WinUI windows from being collected while open and
/// gives the application one place to close secondary HWNDs during shutdown.
/// </summary>
internal static class AppWindowRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Window> Windows = new(StringComparer.Ordinal);

    internal static bool TryGet(string key, out Window? window)
    {
        lock (SyncRoot)
        {
            return Windows.TryGetValue(key, out window);
        }
    }

    internal static void Register(string key, Window window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(window);

        lock (SyncRoot)
        {
            Windows[key] = window;
        }
    }

    internal static void Unregister(string key, Window window)
    {
        lock (SyncRoot)
        {
            if (Windows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
            {
                Windows.Remove(key);
            }
        }
    }

    internal static void CloseAll()
    {
        Window[] snapshot;

        lock (SyncRoot)
        {
            snapshot = [.. Windows.Values];
            Windows.Clear();
        }

        foreach (var window in snapshot)
        {
            try
            {
                if (window.DispatcherQueue.HasThreadAccess)
                {
                    window.Close();
                }
                else
                {
                    _ = window.DispatcherQueue.TryEnqueue(window.Close);
                }
            }
            catch
            {
                // Shutdown cleanup must continue even if one HWND is already gone.
            }
        }
    }
}
