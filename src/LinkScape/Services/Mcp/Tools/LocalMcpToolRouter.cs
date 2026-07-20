public static class LocalMcpToolRouter
{
    public const string WindowsIntentToolName = "windows.intent";

    public static IReadOnlyList<ChatToolStatus> GetTools() =>
    [
        new(WindowsIntentToolName, true, "Routes a natural-language prompt to a safe local browser-data tool."),
        .. BrowserDataToolService.GetTools()
    ];

    public static ChatToolResult Invoke(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments = null)
    {
        arguments ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LocalMcpDiagnostics.Trace("ToolRouter", $"Invoke tool={toolName}, args={string.Join(", ", arguments.Select(pair => $"{pair.Key}={pair.Value}"))}");

        return toolName switch
        {
            WindowsIntentToolName => InvokeWindowsIntent(arguments),
            BrowserDataToolService.HistoryTodayToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryRecentToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryMostVisitedToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.FavoritesSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.FavoritesSearchToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.TabsSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsListToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsAddItemToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsRenameToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsSetStartupToolName => InvokeBrowserTool(toolName, arguments),
            _ => new ChatToolResult(toolName, false, $"Local MCP tool '{toolName}' is not registered.")
        };
    }

    private static ChatToolResult InvokeWindowsIntent(IReadOnlyDictionary<string, string> arguments)
    {
        var prompt = arguments.TryGetValue("prompt", out var value) ? value : string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ChatToolResult(WindowsIntentToolName, false, "A prompt argument is required.");
        }

        if (!BrowserDataToolService.TrySelectToolName(prompt, out var selectedToolName))
        {
            var response = CommandCenterChatService.BuildUnsupportedPromptResponse(prompt);
            return new ChatToolResult(WindowsIntentToolName, false, response.Text);
        }

        var forwardedArguments = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = arguments.TryGetValue("query", out var query) && !string.IsNullOrWhiteSpace(query)
                ? query
                : prompt
        };

        var result = InvokeBrowserTool(selectedToolName, forwardedArguments);
        return new ChatToolResult(WindowsIntentToolName, result.Succeeded, result.Message);
    }

    private static ChatToolResult InvokeBrowserTool(string toolName, IReadOnlyDictionary<string, string> arguments)
    {
        if (!BrowserDataToolService.GetTools().Any(tool => string.Equals(tool.ToolName, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ChatToolResult(toolName, false, $"Local MCP browser data tool '{toolName}' is not registered.");
        }

        var result = BrowserDataToolService.Invoke(toolName, arguments);
        return new ChatToolResult(toolName, true, result.Markdown);
    }
}
