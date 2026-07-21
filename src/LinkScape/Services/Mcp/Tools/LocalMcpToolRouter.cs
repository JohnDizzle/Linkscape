public static class LocalMcpToolRouter
{
    public const string WindowsIntentToolName = "windows.intent";

    public static IReadOnlyList<ChatToolStatus> GetTools() =>
    [
        new(WindowsIntentToolName, true, "Routes a natural-language prompt to a safe local browser-data tool."),
        CreateNavigationStatus(BrowserNavigationToolNames.TabsList, "Lists open browser tabs with IDs, titles, URLs, and selected state."),
        CreateNavigationStatus(BrowserNavigationToolNames.TabsFind, "Finds open tabs by title or URL."),
        CreateNavigationStatus(BrowserNavigationToolNames.TabsActivate, "Activates an existing browser tab."),
        CreateNavigationStatus(BrowserNavigationToolNames.Navigate, "Navigates an existing browser tab to a URL."),
        CreateNavigationStatus(BrowserNavigationToolNames.GoBack, "Navigates backward in a browser tab."),
        CreateNavigationStatus(BrowserNavigationToolNames.GoForward, "Navigates forward in a browser tab."),
        CreateNavigationStatus(BrowserNavigationToolNames.Reload, "Reloads a browser tab."),
        CreateNavigationStatus(BrowserNavigationToolNames.GoHome, "Navigates a browser tab to the configured home URL."),
        CreateNavigationStatus(BrowserNavigationToolNames.HomeGet, "Gets the configured browser home URL."),
        CreateNavigationStatus(BrowserNavigationToolNames.HomeSet, "Sets the browser home URL."),
        CreateNavigationStatus(BrowserNavigationToolNames.TabsOpen, "Opens a URL in a new browser tab."),
        .. BrowserDataToolService.GetTools()
    ];

    private static ChatToolStatus CreateNavigationStatus(string toolName, string readyMessage) =>
        BrowserNavigationService.IsReady
            ? new ChatToolStatus(toolName, true, readyMessage)
            : new ChatToolStatus(toolName, false, "Live browser navigation requires the running LinkScape UI process.");

    public static ChatToolResult Invoke(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments = null)
    {
        arguments ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LocalMcpDiagnostics.Trace("ToolRouter", $"Invoke tool={toolName}, args={string.Join(", ", arguments.Select(pair => $"{pair.Key}={pair.Value}"))}");

        return toolName switch
        {
            WindowsIntentToolName => InvokeWindowsIntent(arguments),
            BrowserNavigationToolNames.TabsList => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.TabsFind => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.TabsActivate => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.Navigate => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.GoBack => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.GoForward => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.Reload => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.GoHome => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.HomeGet => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.HomeSet => InvokeBrowserNavigation(toolName, arguments),
            BrowserNavigationToolNames.TabsOpen => InvokeBrowserNavigation(toolName, arguments),
            BrowserDataToolService.HistoryTodayToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryRecentToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryMostVisitedToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistorySearchToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryPeriodToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.HistoryArchiveToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.FavoritesSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.FavoritesSearchToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.TabsSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.TabsSearchToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsListToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsSummaryToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsAddItemToolName => InvokeBrowserTool(toolName, arguments),
            BrowserDataToolService.CollectionsRemoveItemToolName => InvokeBrowserTool(toolName, arguments),
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

    private static ChatToolResult InvokeBrowserNavigation(string toolName, IReadOnlyDictionary<string, string> arguments)
    {
        if (!BrowserNavigationService.IsReady)
        {
            return new ChatToolResult(
                toolName,
                false,
                "Live browser navigation is only available from the running LinkScape window. External MCP server mode can still read browser data from SQLite.");
        }

        var result = BrowserNavigationService.Invoke(new BrowserNavigationCommand(toolName, arguments));
        return new ChatToolResult(toolName, result.Succeeded, result.Message);
    }
}
