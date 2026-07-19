using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public static class WindowsMcpClientService
{
    private const string ToolName = "windows.mcp";

    public static ChatToolStatus GetStatus()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ChatToolStatus(
                ToolName,
                false,
                "Windows MCP local stdio transport is unavailable because the LinkScape executable path could not be resolved.");
        }

        return new ChatToolStatus(
            ToolName,
            true,
            $"Windows MCP local stdio transport is ready: {System.IO.Path.GetFileName(executablePath)} {LocalMcpProtocol.ServerArgument}");
    }

    public static async Task<ChatToolResult> InvokeToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalMcpDiagnostics.Trace("McpClient", $"InvokeToolAsync tool={toolName}");

        var status = GetStatus();
        if (!status.IsAvailable)
        {
            LocalMcpDiagnostics.Trace("McpClient", $"Status unavailable: {status.Message}");
            return new ChatToolResult(ToolName, false, status.Message);
        }

        try
        {
            return await InvokeLocalServerAsync(toolName, arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LocalMcpDiagnostics.Trace("McpClient", $"Transport exception: {ex}");
            return new ChatToolResult(
                ToolName,
                false,
                $"Windows MCP local stdio transport failed: {ex.Message}");
        }
    }

    private static async Task<ChatToolResult> InvokeLocalServerAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ChatToolResult(ToolName, false, "The LinkScape executable path is not available for local MCP startup.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = LocalMcpProtocol.ServerArgument,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        LocalMcpDiagnostics.Trace("McpClient", $"Started MCP server process pid={process.Id}, file={executablePath}");
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));

        var initializeRequest = LocalMcpProtocol.CreateInitializeRequest(1);
        LocalMcpDiagnostics.TraceJson("McpClient", "initialize.request", initializeRequest);
        await LocalMcpProtocol.WriteMessageAsync(
            process.StandardInput.BaseStream,
            initializeRequest,
            cancellationToken);

        var initializeResponse = await LocalMcpProtocol.ReadMessageAsync(process.StandardOutput.BaseStream, cancellationToken);
        LocalMcpDiagnostics.TraceJson("McpClient", "initialize.response", initializeResponse);
        if (initializeResponse is null)
        {
            return await BuildTransportFailureAsync(process, "The local MCP server did not return an initialize response.", cancellationToken);
        }

        if (TryGetErrorMessage(initializeResponse, out var initializeError))
        {
            return new ChatToolResult(ToolName, false, $"Windows MCP initialize failed: {initializeError}");
        }

        var initializedNotification = LocalMcpProtocol.CreateInitializedNotification();
        LocalMcpDiagnostics.TraceJson("McpClient", "initialized.notification", initializedNotification);
        await LocalMcpProtocol.WriteMessageAsync(
            process.StandardInput.BaseStream,
            initializedNotification,
            cancellationToken);

        var toolCallRequest = LocalMcpProtocol.CreateToolCallRequest(2, toolName, arguments);
        LocalMcpDiagnostics.TraceJson("McpClient", "tools.call.request", toolCallRequest);
        await LocalMcpProtocol.WriteMessageAsync(
            process.StandardInput.BaseStream,
            toolCallRequest,
            cancellationToken);

        process.StandardInput.Close();

        JsonObject? toolResponse;

        do
        {
            toolResponse = await LocalMcpProtocol.ReadMessageAsync(process.StandardOutput.BaseStream, cancellationToken);
            LocalMcpDiagnostics.TraceJson("McpClient", "tools.call.response", toolResponse);
            if (toolResponse is null)
            {
                return await BuildTransportFailureAsync(process, $"The local MCP server closed before returning tool '{toolName}'.", cancellationToken);
            }
        }
        while (toolResponse["id"]?.ToJsonString() != "2");

        if (TryGetErrorMessage(toolResponse, out var toolError))
        {
            return new ChatToolResult(toolName, false, toolError);
        }

        var result = toolResponse["result"] as JsonObject;
        var content = result?["content"] as JsonArray;
        var message = ExtractTextContent(content);
        var isError = result?["isError"]?.GetValue<bool>() == true;
        LocalMcpDiagnostics.Trace("McpClient", $"Tool result parsed. IsError={isError}, TextLength={message.Length}");

        await process.WaitForExitAsync(cancellationToken);

        return new ChatToolResult(
            toolName,
            !isError,
            string.IsNullOrWhiteSpace(message)
                ? $"Windows MCP tool '{toolName}' returned no text content."
                : message);
    }

    private static string ExtractTextContent(JsonArray? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();

        foreach (var item in content.OfType<JsonObject>())
        {
            if (string.Equals(item["type"]?.GetValue<string>(), "text", StringComparison.OrdinalIgnoreCase))
            {
                var text = item["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }
        }

        return string.Join("\n\n", lines);
    }

    private static bool TryGetErrorMessage(JsonObject response, out string message)
    {
        var error = response["error"] as JsonObject;
        message = error?["message"]?.GetValue<string>() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(message);
    }

    private static async Task<ChatToolResult> BuildTransportFailureAsync(
        Process process,
        string message,
        CancellationToken cancellationToken)
    {
        var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
        LocalMcpDiagnostics.Trace("McpClient", $"Transport failure. Message={message} STDERR={errorOutput}");
        var fullMessage = string.IsNullOrWhiteSpace(errorOutput)
            ? message
            : $"{message} {errorOutput.Trim()}";

        return new ChatToolResult(ToolName, false, fullMessage.Trim());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
