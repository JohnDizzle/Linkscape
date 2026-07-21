using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record BrowserNavigationCommand(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record BrowserNavigationResult(
    bool Succeeded,
    string Message);

public static class BrowserNavigationToolNames
{
    public const string TabsList = "browser.tabs.list";
    public const string TabsFind = "browser.tabs.find";
    public const string TabsActivate = "browser.tabs.activate";
    public const string Navigate = "browser.navigate";
    public const string GoBack = "browser.go_back";
    public const string GoForward = "browser.go_forward";
    public const string Reload = "browser.reload";
    public const string GoHome = "browser.go_home";
    public const string HomeGet = "browser.home.get";
    public const string HomeSet = "browser.home.set";
    public const string TabsOpen = "browser.tabs.open";

    public static bool IsNavigationTool(string toolName) =>
        toolName is TabsList or TabsFind or TabsActivate or Navigate or GoBack or GoForward or Reload or GoHome or HomeGet or HomeSet or TabsOpen;
}

public static class BrowserNavigationIpc
{
    private const int ConnectTimeoutMilliseconds = 2_000;
    private static readonly string PipeName = $"LinkScape.BrowserNavigation.{Environment.UserName}";
    private static Func<BrowserNavigationCommand, BrowserNavigationResult>? _handler;
    private static int _started;

    public static void RegisterHandler(Func<BrowserNavigationCommand, BrowserNavigationResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _handler, handler);
    }

    public static void StartServer()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(ListenAsync);
    }

    public static async Task<BrowserNavigationResult> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var request = JsonSerializer.Serialize(new BrowserNavigationCommand(toolName, arguments));
            await writer.WriteLineAsync(request);
            var response = await reader.ReadLineAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(response)
                ? new BrowserNavigationResult(false, "The running LinkScape browser did not return a navigation response.")
                : JsonSerializer.Deserialize<BrowserNavigationResult>(response)
                    ?? new BrowserNavigationResult(false, "The running LinkScape browser returned an invalid navigation response.");
        }
        catch (TimeoutException)
        {
            return new BrowserNavigationResult(false, "The running LinkScape browser is not available for navigation commands.");
        }
        catch (IOException)
        {
            return new BrowserNavigationResult(false, "The running LinkScape browser is not available for navigation commands.");
        }
        catch (Exception ex)
        {
            return new BrowserNavigationResult(false, $"Browser navigation IPC failed: {ex.Message}");
        }
    }

    private static async Task ListenAsync()
    {
        while (true)
        {
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var request = await reader.ReadLineAsync();
            var command = string.IsNullOrWhiteSpace(request)
                ? null
                : JsonSerializer.Deserialize<BrowserNavigationCommand>(request);
            var handler = Volatile.Read(ref _handler);
            var response = command is null
                ? new BrowserNavigationResult(false, "The navigation request was invalid.")
                : handler is null
                    ? new BrowserNavigationResult(false, "The LinkScape browser window is not ready for navigation commands.")
                    : handler(command);

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
    }
}
