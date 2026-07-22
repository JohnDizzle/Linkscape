using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

internal static class LinkerAiChatService
{
    private const int MaxOutputTokens = 700;
    private sealed record ProviderChatCompletion(string Text, string? ResponseId = null);

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

        if (IsPageImagePrompt(prompt) && context?.CaptureActivePageImageAsync is not null)
        {
            var viewportImage = await context.CaptureActivePageImageAsync();
            if (!string.IsNullOrWhiteSpace(viewportImage))
            {
                context = context with { ActivePageImageDataUrl = viewportImage };
            }
        }

        try
        {
            var completion = provider.Id switch
            {
                "openai" => await SubmitOpenAiResponsesAsync("https://api.openai.com/v1/responses", credential, provider, prompt, context, cancellationToken),
                "perplexity" => await SubmitOpenAiResponsesAsync("https://api.perplexity.ai/v1/responses", credential, provider, prompt, context, cancellationToken),
                "azure-openai" => await SubmitAzureOpenAiAsync(credential, provider, prompt, context, cancellationToken),
                "anthropic" => await SubmitAnthropicAsync(credential, provider, prompt, context, cancellationToken),
                "google-gemini" => await SubmitGeminiAsync(credential, provider, prompt, context, cancellationToken),
                "xai" => await SubmitOpenAiChatCompletionsAsync(GetProviderEndpoint(credential, "https://api.x.ai/v1"), credential, provider, prompt, context, cancellationToken),
                _ => new ProviderChatCompletion(string.Empty)
            };

            if (string.IsNullOrWhiteSpace(completion.Text))
            {
                return new CommandCenterChatResponse(
                    $"## {provider.DisplayName} is saved but not connected yet\nLinker has the credentials, but this provider adapter still needs its chat endpoint mapping.",
                    IsError: true,
                    ToolResults: [new ChatToolResult($"linker.ai.{provider.Id}", false, "Provider adapter is not enabled.")]);
            }

            return new CommandCenterChatResponse(
                completion.Text.Trim(),
                ToolResults: [new ChatToolResult($"linker.ai.{provider.Id}", true, $"Answered with {provider.DisplayName} after local LinkScape tools did not match.")],
                ProviderResponseId: completion.ResponseId);
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

    private static async Task<ProviderChatCompletion> SubmitOpenAiResponsesAsync(
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
            ["input"] = BuildResponsesInput(prompt, context),
            ["max_output_tokens"] = MaxOutputTokens
        };

        var previousResponseId = provider.Id == "openai"
            ? context?.PreviousProviderResponseId
            : null;
        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            body["previous_response_id"] = previousResponseId;
        }

        if (provider.Id == "openai" && ShouldEnableWebSearch(prompt, previousResponseId))
        {
            body["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "web_search",
                    ["search_context_size"] = "low"
                }
            };
            body["max_tool_calls"] = 3;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        request.Content = CreateJsonContent(body);

        try
        {
            return await SendAndExtractAsync(request, ExtractResponsesCompletion, cancellationToken);
        }
        catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            body.Remove("previous_response_id");
            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
            retryRequest.Content = CreateJsonContent(body);

            var retryCompletion = await SendAndExtractAsync(retryRequest, ExtractResponsesCompletion, cancellationToken);
            return retryCompletion with
            {
                Text = $"{retryCompletion.Text.Trim()}\n\n_Started a fresh provider chat because the previous OpenAI response id was no longer available._"
            };
        }
    }

    private static async Task<ProviderChatCompletion> SubmitOpenAiChatCompletionsAsync(
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

        return await SendAndExtractAsync(request, root => new ProviderChatCompletion(ExtractChatCompletionText(root)), cancellationToken);
    }

    private static async Task<ProviderChatCompletion> SubmitAzureOpenAiAsync(
        LinkerAiProviderCredential credential,
        LinkerAiProviderDefinition provider,
        string prompt,
        CommandCenterChatContext? context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.Endpoint) || string.IsNullOrWhiteSpace(credential.Deployment))
        {
            return new ProviderChatCompletion("## Azure OpenAI needs setup\nAdd your Azure endpoint and deployment name in the Linker provider key dialog.");
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

        return await SendAndExtractAsync(request, root => new ProviderChatCompletion(ExtractChatCompletionText(root)), cancellationToken);
    }

    private static async Task<ProviderChatCompletion> SubmitAnthropicAsync(
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

        return await SendAndExtractAsync(request, root => new ProviderChatCompletion(ExtractAnthropicText(root)), cancellationToken);
    }

    private static async Task<ProviderChatCompletion> SubmitGeminiAsync(
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

        return await SendAndExtractAsync(request, root => new ProviderChatCompletion(ExtractGeminiText(root)), cancellationToken);
    }

    private static string BuildSystemInstructions(CommandCenterChatContext? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Linker, the assistant inside LinkScape Browser.");
        builder.AppendLine("Local browser tools already had the first chance to answer. For this response, answer the user's general question clearly and briefly.");
        builder.AppendLine("Do not claim you used history, favorites, collections, tabs, or local MCP tools unless the user supplied that information in the prompt.");
        builder.AppendLine("If OpenAI web search is available and the user asks for current facts, use it instead of saying you cannot access real-time information.");
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

        if (!string.IsNullOrWhiteSpace(context?.ActivePageImageDataUrl))
        {
            builder.AppendLine("A cached visual overview of the current page may be attached to current-page questions. Use it as visual context and do not claim that every lazy-loaded or hidden page element is present.");
        }

        return builder.ToString().Trim();
    }

    private static JsonNode BuildResponsesInput(string prompt, CommandCenterChatContext? context)
    {
        var input = new JsonArray();
        if (string.IsNullOrWhiteSpace(context?.PreviousProviderResponseId))
        {
            foreach (var turn in context?.ConversationTurns ?? [])
            {
                if (turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Text))
                {
                    continue;
                }

                input.Add(new JsonObject
                {
                    ["role"] = turn.Role,
                    ["content"] = turn.Text
                });
            }
        }

        if (!ShouldAttachPageImage(prompt, context))
        {
            input.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = prompt
            });
            return input;
        }

        input.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input_text",
                    ["text"] = prompt
                },
                new JsonObject
                {
                    ["type"] = "input_image",
                    ["image_url"] = context!.ActivePageImageDataUrl,
                    ["detail"] = "high"
                }
            }
        });
        return input;
    }

    private static bool ShouldAttachPageImage(string prompt, CommandCenterChatContext? context)
    {
        if (string.IsNullOrWhiteSpace(context?.ActivePageImageDataUrl))
        {
            return false;
        }

        return IsPageImagePrompt(prompt);
    }

    private static bool IsPageImagePrompt(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        return System.Text.RegularExpressions.Regex.IsMatch(
            prompt,
            @"\b(this|current)\s+(page|site|website|article)\b|\b(on|from)\s+(this|the)\s+page\b|\bwhat\s+(?:do\s+you\s+)?see\b|\bpage\s+(?:summary|overview|content)\b|\barticles?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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

    private static async Task<ProviderChatCompletion> SendAndExtractAsync(
        HttpRequestMessage request,
        Func<JsonNode?, ProviderChatCompletion> extractCompletion,
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
        return extractCompletion(root);
    }

    private static ProviderChatCompletion ExtractResponsesCompletion(JsonNode? root) =>
        new(ExtractResponsesText(root), root?["id"]?.GetValue<string>());

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

    private static bool ShouldEnableWebSearch(string prompt, string? previousResponseId)
    {
        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            return true;
        }

        prompt = prompt?.Trim() ?? string.Empty;
        return prompt.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("forecast", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("today", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("tomorrow", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("current", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("latest", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("news", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("score", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("stock", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("price", StringComparison.OrdinalIgnoreCase);
    }
}
