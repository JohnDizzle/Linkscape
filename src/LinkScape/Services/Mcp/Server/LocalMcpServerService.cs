using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public static class LocalMcpServerService
{
    public static async Task<bool> TryRunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!args.Any(static arg => string.Equals(arg, LocalMcpProtocol.ServerArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        LocalMcpDiagnostics.Trace("McpServer", "Server mode activated.");

        HistoryPersistenceService.EnsureDatabase();
        SettingsService.EnsureDatabase();
        FavoritesService.EnsureDatabase();
        TabCollectionService.EnsureDatabase();

        await RunAsync(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            cancellationToken);

        return true;
    }

    private static async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        while (await LocalMcpProtocol.ReadMessageAsync(input, cancellationToken) is { } request)
        {
            LocalMcpDiagnostics.TraceJson("McpServer", "request", request);
            var response = HandleRequest(request);
            if (response is null)
            {
                continue;
            }

            LocalMcpDiagnostics.TraceJson("McpServer", "response", response);
            await LocalMcpProtocol.WriteMessageAsync(output, response, cancellationToken);
        }
    }

    private static JsonObject? HandleRequest(JsonObject request)
    {
        var id = request["id"];
        var method = request["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
        {
            return id is null
                ? null
                : LocalMcpProtocol.CreateErrorResponse(id, -32600, "The request did not include a method.");
        }

        return method switch
        {
            "initialize" => LocalMcpProtocol.CreateSuccessResponse(id, BuildInitializeResult()),
            "notifications/initialized" => null,
            "tools/list" => LocalMcpProtocol.CreateSuccessResponse(id, BuildToolsListResult()),
            "tools/call" => LocalMcpProtocol.CreateSuccessResponse(id, BuildToolCallResult(request["params"] as JsonObject)),
            "ping" => LocalMcpProtocol.CreateSuccessResponse(id, new JsonObject()),
            _ => id is null
                ? null
                : LocalMcpProtocol.CreateErrorResponse(id, -32601, $"The method '{method}' is not supported.")
        };
    }

    private static JsonObject BuildInitializeResult()
    {
        return new JsonObject
        {
            ["protocolVersion"] = LocalMcpProtocol.ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = LocalMcpProtocol.ServerName,
                ["version"] = typeof(LocalMcpServerService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"
            }
        };
    }

    private static JsonObject BuildToolsListResult()
    {
        var tools = new JsonArray();

        foreach (var tool in LocalMcpToolRouter.GetTools())
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.ToolName,
                ["description"] = tool.Message,
                ["inputSchema"] = BuildInputSchema(tool.ToolName)
            });
        }

        return new JsonObject
        {
            ["tools"] = tools
        };
    }

    private static JsonObject BuildToolCallResult(JsonObject? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>() ?? string.Empty;
        var argumentObject = parameters?["arguments"] as JsonObject;
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (argumentObject is not null)
        {
            foreach (var pair in argumentObject)
            {
                if (pair.Value is not null)
                {
                    arguments[pair.Key] = pair.Value.ToString();
                }
            }
        }

        var toolResult = LocalMcpToolRouter.Invoke(toolName, arguments);
        LocalMcpDiagnostics.Trace("McpServer", $"Tool invoked. Name={toolName}, Succeeded={toolResult.Succeeded}, TextLength={toolResult.Message.Length}");
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = toolResult.Message
                }
            },
            ["isError"] = !toolResult.Succeeded
        };
    }

    private static JsonObject BuildInputSchema(string toolName)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        if (BrowserNavigationToolNames.IsNavigationTool(toolName))
        {
            if (toolName is BrowserNavigationToolNames.TabsFind)
            {
                properties["query"] = CreateStringSchema("Tab title or URL search text.");
                required.Add("query");
            }

            if (toolName is BrowserNavigationToolNames.TabsActivate or BrowserNavigationToolNames.Navigate or BrowserNavigationToolNames.GoBack or BrowserNavigationToolNames.GoForward or BrowserNavigationToolNames.Reload or BrowserNavigationToolNames.GoHome)
            {
                properties["tabId"] = CreateStringSchema("Optional browser tab ID. Uses the selected tab when omitted.");
            }

            if (toolName is BrowserNavigationToolNames.Navigate or BrowserNavigationToolNames.HomeSet or BrowserNavigationToolNames.TabsOpen)
            {
                properties["url"] = CreateStringSchema("URL to navigate to, set as home, or open in a new tab.");
                required.Add("url");
            }

            if (toolName == BrowserNavigationToolNames.TabsActivate)
            {
                required.Add("tabId");
            }

            if (toolName == BrowserNavigationToolNames.TabsOpen)
            {
                properties["select"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Whether to activate the new tab. Defaults to true."
                };
            }
        }
        else if (toolName == LocalMcpToolRouter.WindowsIntentToolName)
        {
            properties["prompt"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Natural-language prompt to route to a safe local tool."
            };
            properties["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional search text for favorites or history prompts."
            };
            required.Add("prompt");
        }
        else if (toolName == BrowserDataToolService.FavoritesSearchToolName)
        {
            properties["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Favorites title or URL search text."
            };
            required.Add("query");
        }
        else if (toolName.StartsWith("browser.collections.", StringComparison.OrdinalIgnoreCase))
        {
            properties["collection"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Collection name or id."
            };
            properties["url"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "URL to add or remove from a collection."
            };
            properties["title"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional title for a collection item."
            };
            properties["prompt"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Original chat prompt associated with the request."
            };
        }
        else
        {
            properties["prompt"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Original chat prompt associated with the request."
            };
            properties["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional query text used by the tool."
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = true
        };
    }

    private static JsonObject CreateStringSchema(string description) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description
        };
}
