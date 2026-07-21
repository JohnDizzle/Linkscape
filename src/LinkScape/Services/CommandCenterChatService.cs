using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public static class CommandCenterChatService
{
    public static CommandCenterChatResponse GetStartupSummary()
    {
        var mcpStatus = WindowsMcpClientService.GetStatus();

        var text = $"""
            Local chat services are ready.

            • Windows MCP: {mcpStatus.Message}
            """;

        return new CommandCenterChatResponse(
            text,
            ToolResults:
            [
                new ChatToolResult(mcpStatus.ToolName, mcpStatus.IsAvailable, mcpStatus.Message)
            ]);
    }

    public static async Task<CommandCenterChatResponse> SubmitAsync(
        string prompt,
        CommandCenterChatContext? context = null,
        CancellationToken cancellationToken = default)
    {
        prompt = prompt?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new CommandCenterChatResponse("Type a command or question first.", IsError: true);
        }

        if (IsToolCatalogPrompt(prompt))
        {
            return BuildToolCatalogResponse();
        }

        if (IsBrowserDataPrompt(prompt))
        {
            return await SubmitBrowserDataPromptAsync(prompt, context, cancellationToken);
        }

        if (IsWindowsMcpPrompt(prompt))
        {
            var result = await WindowsMcpClientService.InvokeToolAsync(
                "windows.intent",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["prompt"] = prompt
                },
                cancellationToken);

            return new CommandCenterChatResponse(result.Message, !result.Succeeded, [result]);
        }

        if (LinkerAiCredentialService.HasAnyApiKey())
        {
            return await LinkerAiChatService.SubmitAsync(prompt, context, cancellationToken);
        }

        return BuildUnsupportedPromptResponse(prompt);
    }

    private static CommandCenterChatResponse BuildToolCatalogResponse()
    {
        var mcpStatus = WindowsMcpClientService.GetStatus();
        var tools = LocalMcpToolRouter.GetTools();
        var builder = new StringBuilder();
        builder.AppendLine("## Local MCP tools");
        builder.AppendLine();
        builder.AppendLine($"- Transport: **{(mcpStatus.IsAvailable ? "ready" : "unavailable")}**");
        builder.AppendLine($"- Status: {mcpStatus.Message}");
        builder.AppendLine();
        builder.AppendLine("| Tool | Available | What it does |");
        builder.AppendLine("|---|---:|---|");

        foreach (var tool in tools)
        {
            builder.AppendLine($"| `{tool.ToolName}` | {(tool.IsAvailable ? "yes" : "no")} | {tool.Message} |");
        }

        return new CommandCenterChatResponse(
            builder.ToString().TrimEnd(),
            !mcpStatus.IsAvailable,
            [
                new ChatToolResult(mcpStatus.ToolName, mcpStatus.IsAvailable, mcpStatus.Message),
                .. tools.Select(tool => new ChatToolResult(tool.ToolName, tool.IsAvailable, tool.Message))
            ]);
    }

    private static async Task<CommandCenterChatResponse> SubmitBrowserDataPromptAsync(
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        var query = prompt;
        string toolName;

        if (TryParseBrowserSearchPrompt(prompt, out var parsedToolName, out var parsedQuery))
        {
            toolName = parsedToolName;
            query = parsedQuery;
        }
        else if (!BrowserDataToolService.TrySelectToolName(prompt, out toolName))
        {
            return BuildUnsupportedPromptResponse(prompt);
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = prompt,
            ["query"] = query
        };

        if (!string.IsNullOrWhiteSpace(context?.ActiveUrl))
        {
            arguments["activeUrl"] = context.ActiveUrl;
        }

        if (!string.IsNullOrWhiteSpace(context?.ActiveTitle))
        {
            arguments["activeTitle"] = context.ActiveTitle;
        }

        var mcpResult = await WindowsMcpClientService.InvokeToolAsync(toolName, arguments, cancellationToken);
        if (mcpResult.Succeeded)
        {
            return new CommandCenterChatResponse(mcpResult.Message, ToolResults: [mcpResult]);
        }

        var fallbackResult = BrowserDataToolService.Invoke(toolName, arguments);
        return new CommandCenterChatResponse(
            fallbackResult.Markdown,
            ToolResults:
            [
                mcpResult,
                new ChatToolResult(toolName, true, "Analyzed local browser history/favorites databases with direct local fallback.")
            ]);
    }

    public static bool TryParseBrowserSearchPrompt(string prompt, out string toolName, out string query)
    {
        var match = Regex.Match(
            prompt ?? string.Empty,
            @"\b(?:search|find)\s+(?:for\s+)?(?<query>.+?)\s+(?:in|from)\s+(?:my\s+)?(?<source>favorites?|bookmarks?|history|tabs?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            toolName = string.Empty;
            query = string.Empty;
            return false;
        }

        query = match.Groups["query"].Value.Trim().Trim('"', '\'');
        toolName = match.Groups["source"].Value.ToLowerInvariant() switch
        {
            "favorite" or "favorites" or "bookmark" or "bookmarks" => BrowserDataToolService.FavoritesSearchToolName,
            "history" => BrowserDataToolService.HistorySearchToolName,
            "tab" or "tabs" => BrowserDataToolService.TabsSearchToolName,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(toolName);
    }

    private static bool IsWindowsMcpPrompt(string prompt) =>
        prompt.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("open app", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("window", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("mcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsToolCatalogPrompt(string prompt) =>
        prompt.Contains("tools", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("tool list", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("mcp status", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("what can you do", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("capabilities", StringComparison.OrdinalIgnoreCase);

    private static bool IsBrowserDataPrompt(string prompt) =>
        prompt.Contains("history", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("favorite", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("bookmark", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("collection", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("collections", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("active", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("visited", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("tab", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("database", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("data", StringComparison.OrdinalIgnoreCase);

    public static CommandCenterChatResponse BuildUnsupportedPromptResponse(string prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## I can only answer browser questions right now");
        builder.AppendLine();
        builder.AppendLine("I could not match that request to a local LinkScape browser tool.");
        builder.AppendLine();
        builder.AppendLine("### What I can use");
        builder.AppendLine();
        builder.AppendLine("- **Browser history**: today, recent activity, most visited sites, active pages");
        builder.AppendLine("- **Favorites**: summarize saved favorites or search bookmarks");
        builder.AppendLine("- **Tabs**: summarize the saved tab session and selected/restored tabs");
        builder.AppendLine("- **Collections**: list/show collections, add the current page, remove the current page, or choose a startup collection");
        builder.AppendLine("- **Local MCP tools**: type `mcp status` or `tools` to see the current tool catalog");
        builder.AppendLine();
        builder.AppendLine("### Add a provider key for full chat");
        builder.AppendLine();
        builder.AppendLine("**Want Linker to behave more like a chat agent? Add a provider key from the Linker panel or Settings.**");
        builder.AppendLine();
        builder.AppendLine("After that, Linker will still try local browser tools first, then use your selected provider for general questions.");

        return new CommandCenterChatResponse(
            builder.ToString().TrimEnd(),
            IsError: true,
            ToolResults:
            [
                new ChatToolResult("linkscape.browser.scope", false, $"No browser tool matched prompt: {prompt}")
            ]);
    }

}
