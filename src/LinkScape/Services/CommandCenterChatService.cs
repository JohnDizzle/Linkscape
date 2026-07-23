using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LinkScape.Models;

public static class CommandCenterChatService
{
    private sealed record BrowserNavigationIntent(BrowserNavigationCommand Command, bool ActivateFirstMatch = false);

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

        if (TryParseBrowserNavigationIntent(prompt, out var navigationIntent))
        {
            return SubmitBrowserNavigationPrompt(navigationIntent);
        }

        if (IsCapabilityHelpPrompt(prompt))
        {
            return BuildCapabilityHelpResponse(prompt);
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
            var safetyResult = await LinkerSafetyService.CheckProviderPromptAsync(prompt, cancellationToken);
            if (!safetyResult.IsAllowed)
            {
                return new CommandCenterChatResponse(
                    safetyResult.Message,
                    IsError: true,
                    ToolResults: [new ChatToolResult("linker.safety", false, $"Blocked provider chat prompt: {safetyResult.Category}")]);
            }

            return await LinkerAiChatService.SubmitAsync(prompt, context, cancellationToken);
        }

        var localSafetyResult = await LinkerSafetyService.CheckProviderPromptAsync(prompt, cancellationToken);
        if (!localSafetyResult.IsAllowed)
        {
            return new CommandCenterChatResponse(
                localSafetyResult.Message,
                IsError: true,
                ToolResults: [new ChatToolResult("linker.safety", false, $"Blocked chat prompt: {localSafetyResult.Category}")]);
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

    private static CommandCenterChatResponse BuildCapabilityHelpResponse(string prompt)
    {
        var topic = prompt["linker help".Length..].Trim().ToLowerInvariant();
        var builder = new StringBuilder();

        switch (topic)
        {
            case "navigation":
                builder.AppendLine("## Navigate and browser actions");
                builder.AppendLine();
                builder.AppendLine("Linker can carry out common browser actions from a natural-language request:");
                builder.AppendLine();
                builder.AppendLine("- Open a web address or search");
                builder.AppendLine("- Go back, forward, home, or reload");
                builder.AppendLine("- Create, activate, find, close, or restore tabs");
                builder.AppendLine();
                builder.AppendLine("Try: `open github.com`, `go back`, or `find my GitHub tab`.");
                break;

            case "tabs":
                builder.AppendLine("## Tabs");
                builder.AppendLine();
                builder.AppendLine("Linker can list and summarize open or saved tabs, find a tab by title or address, activate it, and help restore recently closed tabs.");
                builder.AppendLine();
                builder.AppendLine("Try: `show my active tabs` or `find GitHub in my tabs`.");
                break;

            case "history":
                builder.AppendLine("## History");
                builder.AppendLine();
                builder.AppendLine("Linker can show recent activity, find visits, identify most-visited sites, and group history by day, month, or year. Grouped reports can also include favorites and collections.");
                builder.AppendLine();
                builder.AppendLine("Try: `show my recent history` or choose **Groups** inside the History menu.");
                break;

            case "collections":
                builder.AppendLine("## Collections");
                builder.AppendLine();
                builder.AppendLine("Linker can list collections, show the pages in one, add or remove the current page, and use a collection as a startup set.");
                builder.AppendLine();
                builder.AppendLine("Try: `show my collections` or `add the current page to Personal`.");
                break;

            default:
                builder.AppendLine("## What Linker can do");
                builder.AppendLine();
                builder.AppendLine("- **Navigate and act** — open addresses, search, go back or forward, reload, and go home");
                builder.AppendLine("- **Manage tabs** — list, find, activate, create, close, and restore tabs");
                builder.AppendLine("- **Explore history** — review recent activity, search visits, see most-visited sites, and group results");
                builder.AppendLine("- **Use collections** — list collections, inspect them, and add or remove the current page");
                builder.AppendLine("- **Work with favorites** — summarize or search saved favorites");
                builder.AppendLine();
                builder.AppendLine("Choose a topic from **Status** for examples. **Technical status** shows the underlying local tool diagnostics.");
                break;
        }

        return new CommandCenterChatResponse(builder.ToString().TrimEnd());
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
        var tabActions = BuildTabActions(toolName, query);
        if (mcpResult.Succeeded)
        {
            return new CommandCenterChatResponse(
                mcpResult.Message,
                ToolResults: [mcpResult],
                TabActions: tabActions);
        }

        var fallbackResult = BrowserDataToolService.Invoke(toolName, arguments);
        return new CommandCenterChatResponse(
            fallbackResult.Markdown,
            ToolResults:
            [
                mcpResult,
                new ChatToolResult(toolName, true, "Analyzed local browser history/favorites databases with direct local fallback.")
            ],
            TabActions: tabActions);
    }

    private static IReadOnlyList<ChatTabAction>? BuildTabActions(string toolName, string query)
    {
        if (toolName is not BrowserDataToolService.TabsSummaryToolName and not BrowserDataToolService.TabsSearchToolName)
        {
            return null;
        }

        IEnumerable<BrowserTab> tabs = TabPersistenceService.LoadTabs<BrowserTab[]>("tabs") ?? [];
        if (toolName == BrowserDataToolService.TabsSearchToolName && !string.IsNullOrWhiteSpace(query))
        {
            tabs = tabs.Where(tab =>
                tab.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                tab.Url.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return tabs
            .OrderBy(tab => tab.Order)
            .ThenBy(tab => tab.DateTime)
            .Take(20)
            .Select(tab => new ChatTabAction(tab.Id, tab.Title, tab.Url))
            .ToArray();
    }

    private static CommandCenterChatResponse SubmitBrowserNavigationPrompt(BrowserNavigationIntent intent)
    {
        var result = BrowserNavigationService.Invoke(intent.Command);

        if (result.Succeeded && intent.ActivateFirstMatch)
        {
            var firstMatch = result.Message
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            var separatorIndex = firstMatch?.IndexOf(" | ", StringComparison.Ordinal) ?? -1;

            if (separatorIndex <= 0)
            {
                result = new BrowserNavigationResult(false, "No open tab matched that name.");
            }
            else
            {
                var tabId = firstMatch![..separatorIndex];
                result = BrowserNavigationService.Invoke(
                    new BrowserNavigationCommand(
                        BrowserNavigationToolNames.TabsActivate,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["tabId"] = tabId
                        }));
            }
        }

        return new CommandCenterChatResponse(
            result.Message,
            IsError: !result.Succeeded,
            ToolResults: [new ChatToolResult(intent.Command.ToolName, result.Succeeded, result.Message)]);
    }

    public static bool TryParseBrowserSearchPrompt(string prompt, out string toolName, out string query)
    {
        var match = Regex.Match(
            prompt ?? string.Empty,
            @"\b(?:search|find)\s+(?:for\s+)?(?<query>.+?)\s+(?:in|from)\s+(?:my\s+)?(?<source>favorites?|bookmarks?|history|tabs?)\b|\b(?:search|find)\s+(?:my\s+)?(?<source2>favorites?|bookmarks?|history|tabs?)\s+(?:for\s+)?(?<query2>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            toolName = string.Empty;
            query = string.Empty;
            return false;
        }

        query = (match.Groups["query"].Success ? match.Groups["query"].Value : match.Groups["query2"].Value)
            .Trim()
            .Trim('"', '\'');
        var source = match.Groups["source"].Success ? match.Groups["source"].Value : match.Groups["source2"].Value;
        toolName = source.ToLowerInvariant() switch
        {
            "favorite" or "favorites" or "bookmark" or "bookmarks" => BrowserDataToolService.FavoritesSearchToolName,
            "history" => BrowserDataToolService.HistorySearchToolName,
            "tab" or "tabs" => BrowserDataToolService.TabsSearchToolName,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(toolName);
    }

    public static bool TryParseBrowserNavigationPrompt(string prompt, out BrowserNavigationCommand command)
    {
        var parsed = TryParseBrowserNavigationIntent(prompt, out var intent);
        command = parsed
            ? intent.Command
            : new BrowserNavigationCommand(string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        return parsed;
    }

    private static bool TryParseBrowserNavigationIntent(string prompt, out BrowserNavigationIntent intent)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        var normalized = prompt.TrimEnd('.', '!', '?');

        BrowserNavigationIntent Create(string toolName, params (string Key, string Value)[] arguments) =>
            new(new BrowserNavigationCommand(toolName, arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)));

        if (Regex.IsMatch(normalized, @"\b(?:go\s+)?back\b", RegexOptions.IgnoreCase))
        {
            intent = Create(BrowserNavigationToolNames.GoBack);
            return true;
        }

        if (Regex.IsMatch(normalized, @"\b(?:go\s+)?forward\b", RegexOptions.IgnoreCase))
        {
            intent = Create(BrowserNavigationToolNames.GoForward);
            return true;
        }

        if (Regex.IsMatch(normalized, @"\b(?:reload|refresh)(?:\s+(?:this|the)?\s*page)?\b", RegexOptions.IgnoreCase))
        {
            intent = Create(BrowserNavigationToolNames.Reload);
            return true;
        }

        if (Regex.IsMatch(normalized, @"\bgo\s+home\b", RegexOptions.IgnoreCase))
        {
            intent = Create(BrowserNavigationToolNames.GoHome);
            return true;
        }

        var setHomeMatch = Regex.Match(normalized, @"\bset\s+(?:my\s+)?home\s+(?:to\s+)?(?<url>\S+)", RegexOptions.IgnoreCase);
        if (setHomeMatch.Success)
        {
            intent = Create(BrowserNavigationToolNames.HomeSet, ("url", setHomeMatch.Groups["url"].Value));
            return true;
        }

        var tabMatch = Regex.Match(
            normalized,
            @"^(?:(?:active|activate|switch(?:\s+to)?)\s+(?:my\s+|the\s+)?(?<query>.+?)(?:\s+tab)?|go\s+to\s+(?:my\s+|the\s+)?(?<queryWithTab>.+?)\s+tab)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (tabMatch.Success)
        {
            var query = (tabMatch.Groups["query"].Success
                    ? tabMatch.Groups["query"].Value
                    : tabMatch.Groups["queryWithTab"].Value)
                .Trim()
                .Trim('"', '\'');

            intent = new BrowserNavigationIntent(
                new BrowserNavigationCommand(
                    BrowserNavigationToolNames.TabsFind,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["query"] = query
                    }),
                ActivateFirstMatch: true);
            return true;
        }

        var openMatch = Regex.Match(normalized, @"\bopen\s+(?:a\s+)?(?:new\s+)?tab\b(?<target>.*)$", RegexOptions.IgnoreCase);
        if (openMatch.Success)
        {
            var targetUrl = ExtractUrlFromNavigationTarget(openMatch.Groups["target"].Value);
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                intent = Create(BrowserNavigationToolNames.TabsOpen, ("url", targetUrl));
                return true;
            }
        }

        var navigateMatch = Regex.Match(normalized, @"\b(?:go\s+to|navigate\s+to)\s+(?<url>(?:https?://)?[^\s]+\.[^\s]+)", RegexOptions.IgnoreCase);
        if (navigateMatch.Success)
        {
            intent = Create(BrowserNavigationToolNames.Navigate, ("url", navigateMatch.Groups["url"].Value));
            return true;
        }

        intent = default!;
        return false;
    }

    private static string ExtractUrlFromNavigationTarget(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            value,
            @"(?<url>(?:https?://)?(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/[^\s]*)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["url"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?')
            : string.Empty;
    }

    private static bool IsWindowsMcpPrompt(string prompt) =>
        prompt.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("open app", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("window", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("mcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsCapabilityHelpPrompt(string prompt) =>
        prompt.StartsWith("linker help", StringComparison.OrdinalIgnoreCase);

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
