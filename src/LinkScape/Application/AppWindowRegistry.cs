using Microsoft.UI.Xaml;

namespace LinkScape.Application;

internal enum AppWindowKind
{
    WebApp
}

/// <summary>
/// Owns secondary top-level windows created by LinkScape features such as installed web apps.
/// Keeping strong references here prevents WinUI windows from being collected while open and
/// gives the application one place to close secondary HWNDs during shutdown.
/// </summary>
internal static class AppWindowRegistry
{
    private sealed record WindowEntry(Window Window, AppWindowKind Kind, long Sequence);

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, WindowEntry> Windows = new(StringComparer.Ordinal);
    private static long _nextSequence;

    internal static bool TryGet(string key, out Window? window)
    {
        lock (SyncRoot)
        {
            if (Windows.TryGetValue(key, out var entry))
            {
                window = entry.Window;
                return true;
            }

            window = null;
            return false;
        }
    }

    internal static void Register(string key, Window window, AppWindowKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(window);

        lock (SyncRoot)
        {
            Windows[key] = new WindowEntry(window, kind, _nextSequence++);
        }
    }

    internal static int Count(AppWindowKind kind)
    {
        lock (SyncRoot)
        {
            return Windows.Values.Count(entry => entry.Kind == kind);
        }
    }

    internal static IReadOnlyList<TWindow> GetWindows<TWindow>(AppWindowKind kind)
        where TWindow : Window
    {
        lock (SyncRoot)
        {
            return Windows.Values
                .Where(entry => entry.Kind == kind)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => entry.Window)
                .OfType<TWindow>()
                .ToArray();
        }
    }

    internal static void Unregister(string key, Window window)
    {
        lock (SyncRoot)
        {
            if (Windows.TryGetValue(key, out var current) && ReferenceEquals(current.Window, window))
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
            snapshot = [.. Windows.Values.Select(entry => entry.Window)];
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
