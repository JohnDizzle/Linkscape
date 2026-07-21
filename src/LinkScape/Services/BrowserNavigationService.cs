using System.Threading;

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

public static class BrowserNavigationService
{
    private static Func<BrowserNavigationCommand, BrowserNavigationResult>? _handler;

    public static bool IsReady => Volatile.Read(ref _handler) is not null;

    public static void RegisterHandler(Func<BrowserNavigationCommand, BrowserNavigationResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _handler, handler);
    }

    public static BrowserNavigationResult Invoke(BrowserNavigationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var handler = Volatile.Read(ref _handler);

        return handler is null
            ? new BrowserNavigationResult(false, "The LinkScape browser window is not ready for navigation commands.")
            : handler(command);
    }
}
