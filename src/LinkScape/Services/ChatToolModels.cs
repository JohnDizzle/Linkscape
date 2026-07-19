public sealed record CommandCenterChatResponse(
    string Text,
    bool IsError = false,
    IReadOnlyList<ChatToolResult>? ToolResults = null);

public sealed record ChatToolResult(
    string ToolName,
    bool Succeeded,
    string Message);

public sealed record ChatToolStatus(
    string ToolName,
    bool IsAvailable,
    string Message);
