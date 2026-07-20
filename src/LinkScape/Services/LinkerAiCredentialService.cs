using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Credentials;

internal sealed record LinkerAiProviderDefinition(
    string Id,
    string DisplayName,
    string Description,
    bool RequiresEndpoint = false,
    bool RequiresDeployment = false,
    string EndpointPlaceholder = "",
    string DeploymentPlaceholder = "",
    string DefaultModel = "");

internal sealed record LinkerAiProviderCredential(
    string ProviderId,
    string ApiKey,
    string Endpoint,
    string Deployment);

internal sealed record LinkerAiKeyTestResult(bool Succeeded, string Message);

internal static class LinkerAiCredentialService
{
    public const string ConfiguredSettingKey = "linker.ai.configured";
    public const string ProviderSettingKey = "linker.ai.provider";
    private const string VaultResourcePrefix = "LinkScape.Linker.Provider";
    private const string VaultUserName = "default";

    public static IReadOnlyList<LinkerAiProviderDefinition> Providers { get; } =
    [
        new("openai", "OpenAI", "Use an OpenAI API key for general Linker answers and future planning.", DeploymentPlaceholder: "gpt-4.1-mini", DefaultModel: "gpt-4.1-mini"),
        new("azure-openai", "Azure OpenAI", "Use an Azure OpenAI resource key and endpoint.", RequiresEndpoint: true, RequiresDeployment: true, "https://your-resource.openai.azure.com", "Deployment name"),
        new("perplexity", "Perplexity", "Use Perplexity for web-grounded agent responses and model fallback.", DeploymentPlaceholder: "perplexity/sonar", DefaultModel: "perplexity/sonar"),
        new("anthropic", "Anthropic Claude", "Use an Anthropic API key for Claude models.", DeploymentPlaceholder: "claude-sonnet-4-20250514", DefaultModel: "claude-sonnet-4-20250514"),
        new("google-gemini", "Google Gemini", "Use a Gemini API key from Google AI Studio.", DeploymentPlaceholder: "gemini-3.5-flash", DefaultModel: "gemini-3.5-flash"),
        new("xai", "xAI", "Use an xAI key with its OpenAI-compatible endpoint.", RequiresEndpoint: true, DeploymentPlaceholder: "grok-4", DefaultModel: "grok-4"),
        new("copilot-studio", "Copilot Studio", "Save Copilot Studio channel credentials for a future bot/channel adapter.", RequiresEndpoint: true, RequiresDeployment: true, "Copilot endpoint or bot URL", "Bot/channel name")
    ];

    public static LinkerAiProviderDefinition SelectedProvider =>
        GetProvider(SettingsService.GetValueOrDefault(ProviderSettingKey, Providers[0].Id));

    public static LinkerAiProviderDefinition GetProvider(string? providerId) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
            ?? Providers[0];

    public static bool HasAnyApiKey() =>
        Providers.Any(provider => HasApiKey(provider.Id));

    public static bool HasApiKey(string providerId)
    {
        try
        {
            return GetCredential(providerId) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveCredential(string providerId, string apiKey, string? endpoint = null, string? deployment = null)
    {
        var provider = GetProvider(providerId);
        var trimmedKey = apiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            return;
        }

        DeleteApiKey(provider.Id);

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(GetVaultResource(provider.Id), VaultUserName, trimmedKey));

        SettingsService.SetValue(ProviderSettingKey, provider.Id);
        SettingsService.SetValue(ConfiguredSettingKey, "true");
        SettingsService.SetValue(GetEndpointSettingKey(provider.Id), endpoint?.Trim() ?? string.Empty);
        SettingsService.SetValue(GetDeploymentSettingKey(provider.Id), deployment?.Trim() ?? string.Empty);
    }

    public static LinkerAiProviderCredential? GetCredential(string providerId)
    {
        var provider = GetProvider(providerId);
        var apiKey = GetApiKey(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new LinkerAiProviderCredential(
            provider.Id,
            apiKey,
            SettingsService.GetValueOrDefault(GetEndpointSettingKey(provider.Id), string.Empty),
            SettingsService.GetValueOrDefault(GetDeploymentSettingKey(provider.Id), string.Empty));
    }

    public static string GetConfiguredEndpoint(string providerId) =>
        SettingsService.GetValueOrDefault(GetEndpointSettingKey(GetProvider(providerId).Id), string.Empty);

    public static string GetConfiguredDeployment(string providerId) =>
        SettingsService.GetValueOrDefault(GetDeploymentSettingKey(GetProvider(providerId).Id), string.Empty);

    public static void DeleteApiKey(string providerId)
    {
        var provider = GetProvider(providerId);
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(GetVaultResource(provider.Id), VaultUserName);
            vault.Remove(credential);
        }
        catch
        {
        }

        SettingsService.SetValue(ConfiguredSettingKey, HasAnyApiKey() ? "true" : "false");
    }

    public static async Task<LinkerAiKeyTestResult> TestSelectedProviderAsync(CancellationToken cancellationToken = default) =>
        await TestProviderAsync(SelectedProvider.Id, cancellationToken);

    public static async Task<LinkerAiKeyTestResult> TestProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId);
        var credential = GetCredential(provider.Id);
        if (credential is null)
        {
            return new LinkerAiKeyTestResult(false, $"No {provider.DisplayName} key is saved yet.");
        }

        if (provider.RequiresEndpoint && string.IsNullOrWhiteSpace(credential.Endpoint))
        {
            return new LinkerAiKeyTestResult(false, $"{provider.DisplayName} needs an endpoint before Linker can test it.");
        }

        try
        {
            return provider.Id switch
            {
                "openai" => await TestBearerModelsEndpointAsync("https://api.openai.com/v1/models", credential.ApiKey, provider.DisplayName, cancellationToken),
                "perplexity" => await TestBearerModelsEndpointAsync("https://api.perplexity.ai/v1/models", credential.ApiKey, provider.DisplayName, cancellationToken),
                "anthropic" => await TestAnthropicAsync(credential.ApiKey, cancellationToken),
                "google-gemini" => await TestGeminiAsync(credential.ApiKey, cancellationToken),
                "azure-openai" => await TestAzureOpenAiAsync(credential, cancellationToken),
                _ => new LinkerAiKeyTestResult(true, $"{provider.DisplayName} credentials were saved. A live adapter has not been enabled yet.")
            };
        }
        catch (OperationCanceledException)
        {
            return new LinkerAiKeyTestResult(false, "The API key test was canceled.");
        }
        catch (Exception ex)
        {
            return new LinkerAiKeyTestResult(false, $"{provider.DisplayName} could not be reached: {ex.Message}");
        }
    }

    private static async Task<LinkerAiKeyTestResult> TestBearerModelsEndpointAsync(
        string modelsEndpoint,
        string apiKey,
        string providerName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendTestRequestAsync(request, providerName, cancellationToken);
    }

    private static async Task<LinkerAiKeyTestResult> TestAzureOpenAiAsync(
        LinkerAiProviderCredential credential,
        CancellationToken cancellationToken)
    {
        var endpoint = credential.Endpoint.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/openai/models?api-version=2024-10-21");
        request.Headers.Add("api-key", credential.ApiKey);
        return await SendTestRequestAsync(request, "Azure OpenAI", cancellationToken);
    }

    private static async Task<LinkerAiKeyTestResult> TestAnthropicAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return await SendTestRequestAsync(request, "Anthropic Claude", cancellationToken);
    }

    private static async Task<LinkerAiKeyTestResult> TestGeminiAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers.Add("x-goog-api-key", apiKey);
        return await SendTestRequestAsync(request, "Google Gemini", cancellationToken);
    }

    private static async Task<LinkerAiKeyTestResult> SendTestRequestAsync(
        HttpRequestMessage request,
        string providerName,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new LinkerAiKeyTestResult(true, $"{providerName} credentials are saved and reachable.");
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = TryExtractProviderError(responseText);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{providerName} returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : detail;

        return new LinkerAiKeyTestResult(false, message);
    }

    private static string? GetApiKey(string providerId)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(GetVaultResource(providerId), VaultUserName);
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch
        {
            return null;
        }
    }

    private static string TryExtractProviderError(string responseText)
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

    private static string GetVaultResource(string providerId) =>
        $"{VaultResourcePrefix}.{GetProvider(providerId).Id}";

    private static string GetEndpointSettingKey(string providerId) =>
        $"linker.ai.{GetProvider(providerId).Id}.endpoint";

    private static string GetDeploymentSettingKey(string providerId) =>
        $"linker.ai.{GetProvider(providerId).Id}.deployment";
}
