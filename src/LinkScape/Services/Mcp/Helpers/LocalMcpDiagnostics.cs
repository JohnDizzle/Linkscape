using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

public static class LocalMcpDiagnostics
{
    private const int MaxEntries = 200;
    private static readonly object SyncRoot = new();
    private static readonly Queue<string> Entries = new();
    private static bool LogFileInitialized;

    public static event Action? EntriesChanged;

    public static IReadOnlyList<string> GetEntries()
    {
        lock (SyncRoot)
        {
            return Entries.ToArray();
        }
    }

    [Conditional("DEBUG")]
    public static void Trace(string source, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{source}] {message}";
        Debug.WriteLine(line);
        TryAppendToLogFile(line);

        lock (SyncRoot)
        {
            Entries.Enqueue(line);

            while (Entries.Count > MaxEntries)
            {
                Entries.Dequeue();
            }
        }

        EntriesChanged?.Invoke();
    }

    [Conditional("DEBUG")]
    public static void TraceJson(string source, string label, JsonObject? payload)
    {
        Trace(source, $"{label}: {payload?.ToJsonString() ?? "<null>"}");
    }

    private static void TryAppendToLogFile(string line)
    {
        try
        {
            var logPath = Path.Combine(LinkScapeCachePaths.CacheDirectory, "mcp-debug.log");
            EnsureLogFileInitialized(logPath);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void EnsureLogFileInitialized(string logPath)
    {
        lock (SyncRoot)
        {
            if (LogFileInitialized)
            {
                return;
            }

            File.WriteAllText(
                logPath,
                $"{DateTime.Now:O} LinkScape MCP debug log started.{Environment.NewLine}");
            LogFileInitialized = true;
        }
    }
}
