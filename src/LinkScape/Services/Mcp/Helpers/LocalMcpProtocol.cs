using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public static class LocalMcpProtocol
{
    public const string ServerArgument = "--mcp-server";
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName = "LinkScape Local MCP";

    public static JsonObject CreateInitializeRequest(int id)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "LinkScape",
                    ["version"] = GetVersion()
                }
            }
        };
    }

    public static JsonObject CreateInitializedNotification()
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };
    }

    public static JsonObject CreateToolCallRequest(int id, string toolName, IReadOnlyDictionary<string, string> arguments)
    {
        var argumentObject = new JsonObject();

        foreach (var pair in arguments)
        {
            argumentObject[pair.Key] = pair.Value;
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = argumentObject
            }
        };
    }

    public static JsonObject CreateSuccessResponse(JsonNode? id, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneNode(id),
            ["result"] = result
        };
    }

    public static JsonObject CreateErrorResponse(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneNode(id),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    public static async Task WriteMessageAsync(Stream output, JsonObject message, CancellationToken cancellationToken = default)
    {
        var payload = message.ToJsonString();
        var body = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(body, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public static async Task<JsonObject?> ReadMessageAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var contentLength = 0;

        while (true)
        {
            var line = await ReadAsciiLineAsync(input, cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var headerName = line[..separatorIndex].Trim();
            var headerValue = line[(separatorIndex + 1)..].Trim();

            if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.TryParse(headerValue, out var parsedLength)
                    ? parsedLength
                    : 0;
            }
        }

        if (contentLength <= 0)
        {
            return null;
        }

        var body = await ReadExactlyAsync(input, contentLength, cancellationToken);
        if (body.Length != contentLength)
        {
            return null;
        }

        var payload = Encoding.UTF8.GetString(body);
        return JsonNode.Parse(payload) as JsonObject;
    }

    public static JsonNode? CloneNode(JsonNode? node)
    {
        return node is null ? null : JsonNode.Parse(node.ToJsonString());
    }

    private static async Task<string?> ReadAsciiLineAsync(Stream input, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();

        while (true)
        {
            var buffer = new byte[1];
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return bytes.Count == 0
                    ? null
                    : Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }

            if (buffer[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }

            bytes.Add(buffer[0]);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream input, int contentLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[contentLength];
        var totalRead = 0;

        while (totalRead < contentLength)
        {
            var bytesRead = await input.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        return totalRead == contentLength
            ? buffer
            : buffer[..totalRead];
    }

    private static string GetVersion()
    {
        return typeof(LocalMcpProtocol).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
