using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

internal static class LinkerAiChatService
{
    private const int MaxOutputTokens = 700;

    public static async Task<CommandCenterChatResponse> SubmitAsync(
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken = default)
    {
        var provider = LinkerAiCredentialService.SelectedProvider;
        var credential = LinkerAiCredentialService.GetCredential(provider.Id);
        if (credential is null)
        {
            return CommandCenterChatService.BuildUnsupportedPromptResponse(prompt);
        }

        try
        {
            var answer = provider.Id switch
            {
                "openai" => await SubmitOpenAiResponsesAsync("https://api.openai.com/v1/responses", credential, provider, prompt, context, cancellationToken),
                "perplexity" => await SubmitOpenAiResponsesAsync("https://api.perplexity.ai/v1/responses", credential, provider, prompt, context, cancellationToken),
                "azure-openai" => await SubmitAzureOpenAiAsync(credential, provider, prompt, context, cancellationToken),
                "anthropic" => await SubmitAnthropicAsync(credential, provider, prompt, context, cancellationToken),
                "google-gemini" => await SubmitGeminiAsync(credential, provider, prompt, context, cancellationToken),
                "xai" => await SubmitOpenAiChatCompletionsAsync(GetProviderEndpoint(credential, "https://api.x.ai/v1"), credential, provider, prompt, context, cancellationToken),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(answer))
            {
                return new CommandCenterChatResponse(
                    $"## {provider.DisplayName} is saved but not connected yet\nLinker has the credentials, but this provider adapter still needs its chat endpoint mapping.",
                    IsError: true,
                    ToolResults: [new ChatToolResult($"linker.ai.{provider.Id}", false, "Provider adapter is not enabled.")]);
            }

            return new CommandCenterChatResponse(
                answer.Trim(),
                ToolResults: [new ChatToolResult($"linker.ai.{provider.Id}", true, $"Answered with {provider.DisplayName} after local LinkScape tools did not match.")]);
        }
        catch (OperationCanceledException)
        {
            return new CommandCenterChatResponse("The provider request was canceled.", IsError: true);
        }
        catch (Exception ex)
        {
            return new CommandCenterChatResponse(
                $"## {provider.DisplayName} could not answer\n{ex.Message}",
                IsError: true,
                ToolResults: [new ChatToolResult($"linker.ai.{provider.Id}", false, ex.Message)]);
        }
    }

    private static async Task<string> SubmitOpenAiResponsesAsync(
        string endpoint,
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = GetModel(credential, provider),
            ["instructions"] = BuildSystemInstructions(context),
            ["input"] = prompt,
            ["max_output_tokens"] = MaxOutputTokens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        request.Content = CreateJsonContent(body);

        return await SendAndExtractAsync(request, ExtractResponsesText, cancellationToken);
    }

    private static async Task<string> SubmitOpenAiChatCompletionsAsync(
        string endpointRoot,
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = GetModel(credential, provider),
            ["messages"] = BuildMessages(BuildSystemInstructions(context), prompt),
            ["max_tokens"] = MaxOutputTokens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpointRoot.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        request.Content = CreateJsonContent(body);

        return await SendAndExtractAsync(request, ExtractChatCompletionText, cancellationToken);
    }

    private static async Task<string> SubmitAzureOpenAiAsync(
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.Endpoint) || string.IsNullOrWhiteSpace(credential.Deployment))
        {
            return "## Azure OpenAI needs setup\nAdd your Azure endpoint and deployment name in the Linker provider key dialog.";
        }

        var endpoint = credential.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(credential.Deployment);
        var body = new JsonObject
        {
            ["messages"] = BuildMessages(BuildSystemInstructions(context), prompt),
            ["max_tokens"] = MaxOutputTokens,
            ["temperature"] = 0.4
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", credential.ApiKey);
        request.Content = CreateJsonContent(body);

        return await SendAndExtractAsync(request, ExtractChatCompletionText, cancellationToken);
    }

    private static async Task<string> SubmitAnthropicAsync(
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = GetModel(credential, provider),
            ["max_tokens"] = MaxOutputTokens,
            ["system"] = BuildSystemInstructions(context),
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", credential.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = CreateJsonContent(body);

        return await SendAndExtractAsync(request, ExtractAnthropicText, cancellationToken);
    }

    private static async Task<string> SubmitGeminiAsync(
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        var model = Uri.EscapeDataString(GetModel(credential, provider));
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = BuildSystemInstructions(context) }
                }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = prompt }
                    }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = MaxOutputTokens,
                ["temperature"] = 0.4
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", credential.ApiKey);
        request.Content = CreateJsonContent(body);

        return await SendAndExtractAsync(request, ExtractGeminiText, cancellationToken);
    }

    private static string BuildSystemInstructions(CommandCenterChatContext? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Linker, the assistant inside LinkScape Browser.");
        builder.AppendLine("Local browser tools already had the first chance to answer. For this response, answer the user's general question clearly and briefly.");
        builder.AppendLine("Do not claim you used history, favorites, collections, tabs, or local MCP tools unless the user supplied that information in the prompt.");
        builder.AppendLine("Use markdown. If a browser-related action requires local tools, tell the user which Linker tool prompt to try.");

        if (!string.IsNullOrWhiteSpace(context?.ActiveTitle) || !string.IsNullOrWhiteSpace(context?.ActiveUrl))
        {
            builder.AppendLine();
            builder.AppendLine("Current browser context:");
            if (!string.IsNullOrWhiteSpace(context.ActiveTitle))
            {
                builder.AppendLine($"Title: {context.ActiveTitle}");
            }

            if (!string.IsNullOrWhiteSpace(context.ActiveUrl))
            {
                builder.AppendLine($"URL: {context.ActiveUrl}");
            }
        }

        return builder.ToString().Trim();
    }

    private static JsonArray BuildMessages(string system, string prompt) =>
    [
        new JsonObject
        {
            ["role"] = "system",
            ["content"] = system
        },
        new JsonObject
        {
            ["role"] = "user",
            ["content"] = prompt
        }
    ];

    private static StringContent CreateJsonContent(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<string> SendAndExtractAsync(
        HttpRequestMessage request,
        Func<JsonNode?, string> extractText,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractProviderError(responseText);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"Provider returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : detail);
        }

        var root = JsonNode.Parse(responseText);
        return extractText(root);
    }

    private static string ExtractResponsesText(JsonNode? root)
    {
        var direct = root?["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return string.Join(
            "\n",
            root?["output"]?.AsArray()
                .SelectMany(item => item?["content"]?.AsArray() ?? [])
                .Select(content => content?["text"]?.GetValue<string>() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text)) ?? []);
    }

    private static string ExtractChatCompletionText(JsonNode? root) =>
        root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;

    private static string ExtractAnthropicText(JsonNode? root) =>
        string.Join(
            "\n",
            root?["content"]?.AsArray()
                .Select(content => content?["text"]?.GetValue<string>() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text)) ?? []);

    private static string ExtractGeminiText(JsonNode? root) =>
        string.Join(
            "\n",
            root?["candidates"]?.AsArray()
                .SelectMany(candidate => candidate?["content"]?["parts"]?.AsArray() ?? [])
                .Select(part => part?["text"]?.GetValue<string>() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text)) ?? []);

    private static string ExtractProviderError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        try
        {
            var root = JsonNode.Parse(responseText);
            return root?["error"]?["message"]?.GetValue<string>()
                ?? root?["error"]?.GetValue<string>()
                ?? root?["message"]?.GetValue<string>()
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetModel(LinkerAiProviderCredential credential, LinkerAiProviderDefinition provider) =>
        string.IsNullOrWhiteSpace(credential.Deployment)
            ? provider.DefaultModel
            : credential.Deployment;

    private static string GetProviderEndpoint(LinkerAiProviderCredential credential, string defaultEndpoint) =>
        string.IsNullOrWhiteSpace(credential.Endpoint) ? defaultEndpoint : credential.Endpoint;
}
