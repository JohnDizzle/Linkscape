using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed record LinkerSafetyResult(
    bool IsAllowed,
    string Message = "",
    string Category = "");

internal static class LinkerSafetyService
{
    private static readonly (string Category, Regex Pattern)[] LocalStopPatterns =
    [
        ("demeaning or hateful language", new Regex(@"\b(slur|hate\s+speech|inferior\s+race|kill\s+all|dehumaniz(?:e|ing)|worthless\s+people)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("harassment or threats", new Regex(@"\b(you\s+should\s+die|go\s+die|threaten|harass|bully|doxx|stalk)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("violent content", new Regex(@"\b(graphic\s+violence|gore|torture|murder\s+method|hurt\s+someone|how\s+to\s+kill|build\s+a\s+weapon|stab|shoot\s+someone)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("sexual content", new Regex(@"\b(explicit\s+sexual|porn|erotic|sexually\s+explicit|nude\s+minor|sexual\s+minor)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
    ];

    public static async Task<LinkerSafetyResult> CheckProviderPromptAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new LinkerSafetyResult(true);
        }

        var localResult = CheckLocal(prompt);
        if (!localResult.IsAllowed)
        {
            return localResult;
        }

        var openAiCredential = LinkerAiCredentialService.GetCredential("openai");
        if (openAiCredential is null)
        {
            return new LinkerSafetyResult(true);
        }

        try
        {
            return await CheckOpenAiModerationAsync(prompt, openAiCredential.ApiKey, cancellationToken);
        }
        catch
        {
            return new LinkerSafetyResult(true);
        }
    }

    public static string BuildStopResponse(string category)
    {
        var label = string.IsNullOrWhiteSpace(category) ? "unsafe content" : category;
        return $"""
            ## STOP

            I can't help with that request because it appears to include **{label}**.

            Linker can help with browser history, favorites, tabs, collections, safe web questions, and general chat when a provider key is connected.
            """.Trim();
    }

    private static LinkerSafetyResult CheckLocal(string prompt)
    {
        foreach (var (category, pattern) in LocalStopPatterns)
        {
            if (pattern.IsMatch(prompt))
            {
                return new LinkerSafetyResult(false, BuildStopResponse(category), category);
            }
        }

        return new LinkerSafetyResult(true);
    }

    private static async Task<LinkerSafetyResult> CheckOpenAiModerationAsync(
        string prompt,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = "omni-moderation-latest",
            ["input"] = prompt
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/moderations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new LinkerSafetyResult(true);
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = JsonNode.Parse(responseText);
        var result = root?["results"]?[0];
        if (result?["flagged"]?.GetValue<bool>() != true)
        {
            return new LinkerSafetyResult(true);
        }

        var category = GetFirstFlaggedCategory(result["categories"] as JsonObject);
        return new LinkerSafetyResult(false, BuildStopResponse(category), category);
    }

    private static string GetFirstFlaggedCategory(JsonObject? categories)
    {
        if (categories is null)
        {
            return "unsafe content";
        }

        foreach (var preferredCategory in new[]
        {
            "sexual/minors",
            "sexual",
            "violence/graphic",
            "violence",
            "hate/threatening",
            "hate",
            "harassment/threatening",
            "harassment",
            "self-harm/instructions",
            "self-harm/intent",
            "self-harm",
            "illicit/violent",
            "illicit"
        })
        {
            if (categories[preferredCategory]?.GetValue<bool>() == true)
            {
                return preferredCategory.Replace("/", " / ", StringComparison.Ordinal);
            }
        }

        return "unsafe content";
    }
}
