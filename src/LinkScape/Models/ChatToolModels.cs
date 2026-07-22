using System.Threading.Tasks;

public sealed record CommandCenterChatResponse(
    string Text,
    bool IsError = false,
    IReadOnlyList<ChatToolResult>? ToolResults = null,
    string? ProviderResponseId = null);

public sealed record ChatToolResult(
    string ToolName,
    bool Succeeded,
    string Message);

public sealed record ChatToolStatus(
    string ToolName,
    bool IsAvailable,
    string Message);

public sealed record CommandCenterChatTurn(
    string Role,
    string Text);

public sealed record CommandCenterChatContext(
    string? ActiveUrl = null,
    string? ActiveTitle = null,
    string? PreviousProviderResponseId = null,
    string? ActivePageImageDataUrl = null,
    IReadOnlyList<CommandCenterChatTurn>? ConversationTurns = null,
    string? ActiveTabId = null,
    Func<Task<string?>>? CaptureActivePageImageAsync = null);
