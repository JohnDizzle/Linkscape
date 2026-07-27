internal enum AzureOpenAiEndpointKind
{
    LegacyDeployment,
    OpenAiV1
}

internal sealed record AzureOpenAiEndpointInfo(
    AzureOpenAiEndpointKind Kind,
    string BaseUrl);

internal static class AzureOpenAiEndpoint
{
    private const string LegacyApiVersion = "2024-10-21";
    private const string LegacyResponsesApiVersion = "2024-06-01";
    private const string OpenAiV1Path = "/openai/v1";

    public static bool TryCreate(string? endpoint, out AzureOpenAiEndpointInfo info, out string error)
    {
        info = new AzureOpenAiEndpointInfo(AzureOpenAiEndpointKind.LegacyDeployment, string.Empty);
        error = string.Empty;

        var value = endpoint?.Trim().TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Azure OpenAI needs an endpoint before Linker can test it.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            error = "Azure OpenAI endpoint must be an absolute HTTPS URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Azure OpenAI endpoint must use HTTPS.";
            return false;
        }

        if (uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            error = "This is a Foundry project endpoint. Use your Azure OpenAI endpoint, such as https://your-resource.openai.azure.com or https://your-resource.openai.azure.com/openai/v1.";
            return false;
        }

        if (uri.AbsolutePath.Equals(OpenAiV1Path, StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith($"{OpenAiV1Path}/", StringComparison.OrdinalIgnoreCase))
        {
            info = new AzureOpenAiEndpointInfo(
                AzureOpenAiEndpointKind.OpenAiV1,
                $"{uri.Scheme}://{uri.Authority}{OpenAiV1Path}");
            return true;
        }

        info = new AzureOpenAiEndpointInfo(AzureOpenAiEndpointKind.LegacyDeployment, value);
        return true;
    }

    public static string BuildModelsUrl(AzureOpenAiEndpointInfo endpoint) =>
        endpoint.Kind == AzureOpenAiEndpointKind.OpenAiV1
            ? $"{endpoint.BaseUrl}/models"
            : $"{endpoint.BaseUrl}/openai/models?api-version={LegacyApiVersion}";

    public static string BuildChatCompletionsUrl(AzureOpenAiEndpointInfo endpoint, string deployment)
    {
        if (endpoint.Kind == AzureOpenAiEndpointKind.OpenAiV1)
        {
            return $"{endpoint.BaseUrl}/chat/completions";
        }

        return $"{endpoint.BaseUrl}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={LegacyApiVersion}";
    }

    public static string BuildResponsesUrl(AzureOpenAiEndpointInfo endpoint) =>
        endpoint.Kind == AzureOpenAiEndpointKind.OpenAiV1
            ? $"{endpoint.BaseUrl}/responses"
            : $"{endpoint.BaseUrl}/openai/responses?api-version={LegacyResponsesApiVersion}";
}
