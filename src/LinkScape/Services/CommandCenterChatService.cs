using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class CommandCenterChatService
{
    public static CommandCenterChatResponse GetStartupSummary()
    {
        var reactorStatus = ReactorReferenceService.GetStatus();
        var mcpStatus = WindowsMcpClientService.GetStatus();
        var onlineStatus = OnlineReferenceService.GetStatus();

        var text = $"""
            Local chat services are ready.

            • Reactor local reference: {reactorStatus.Message}
            • Windows MCP: {mcpStatus.Message}
            • Online reference: {onlineStatus.Message}
            """;

        return new CommandCenterChatResponse(
            text,
            ToolResults:
            [
                new ChatToolResult("reactor.status", reactorStatus.SourceExists, reactorStatus.Message),
                new ChatToolResult(mcpStatus.ToolName, mcpStatus.IsAvailable, mcpStatus.Message),
                new ChatToolResult(onlineStatus.ToolName, onlineStatus.IsAvailable, onlineStatus.Message)
            ]);
    }

    public static async Task<CommandCenterChatResponse> SubmitAsync(
        string prompt,
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
            return await SubmitBrowserDataPromptAsync(prompt, cancellationToken);
        }

        if (IsReactorReferencePrompt(prompt))
        {
            return SearchReactor(prompt);
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

        var onlineResult = OnlineReferenceService.BuildReactorReference(prompt);
        return new CommandCenterChatResponse(
            $"I do not have a local model connected yet. For now, I can route framework questions to online reference links.\n\n{onlineResult.Message}",
            ToolResults: [onlineResult]);
    }

    private static CommandCenterChatResponse SearchReactor(string prompt)
    {
        var query = ExtractSearchQuery(prompt);
        var localResults = ReactorReferenceService.Search(query, 6);

        if (localResults.Count == 0)
        {
            var onlineResult = OnlineReferenceService.BuildReactorReference(query);
            return new CommandCenterChatResponse(
                $"No local Reactor results were found for '{query}'.\n\n{onlineResult.Message}",
                ToolResults: [onlineResult]);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Local Reactor results for '{query}':");
        builder.AppendLine();

        foreach (var result in localResults)
        {
            builder.AppendLine($"• {result.RelativePath}:{result.LineNumber}");
            builder.AppendLine($"  {result.Preview}");
        }

        return new CommandCenterChatResponse(
            builder.ToString().TrimEnd(),
            ToolResults: [new ChatToolResult("reactor.search", true, $"Found {localResults.Count} local result(s).")]);
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
        CancellationToken cancellationToken)
    {
         var toolName = BrowserDataToolService.SelectToolName(prompt);
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = prompt,
            ["query"] = prompt
        };

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

    private static bool IsReactorReferencePrompt(string prompt) =>
        prompt.Contains("reactor", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("usestate", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("component", StringComparison.OrdinalIgnoreCase);

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
        prompt.Contains("today", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("active", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("visited", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("database", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("data", StringComparison.OrdinalIgnoreCase);

    private static string ExtractSearchQuery(string prompt)
    {
        var query = prompt
            .Replace("search", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("reactor", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("for", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(query) ? "UseState" : query;
    }
}
