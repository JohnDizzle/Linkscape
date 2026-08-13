using System.Text.Json.Nodes;

namespace LinkScape.Tests;

[TestClass]
public sealed class LinkerAiChatServiceTests
{
    [TestMethod]
    public void BuildAzureOpenAiResponsesBody_AttachesPageImageDataUrl()
    {
        var body = LinkerAiChatService.BuildAzureOpenAiResponsesBody(
            new LinkerAiProviderCredential(
                "azure-openai",
                "test-key",
                "https://intrafizaifactory.services.ai.azure.com/openai/v1",
                "gpt-5.1"),
            "how many installs on this page?",
            new CommandCenterChatContext(
                ActiveUrl: "https://partner.microsoft.com/dashboard",
                ActiveTitle: "Partner Center",
                ActivePageImageDataUrl: "data:image/png;base64,abc123"));

        Assert.AreEqual("gpt-5.1", body["model"]?.GetValue<string>());
        Assert.AreEqual(700, body["max_output_tokens"]?.GetValue<int>());

        var content = body["input"]?[0]?["content"]?.AsArray();
        Assert.IsNotNull(content);
        Assert.AreEqual("input_text", content![0]?["type"]?.GetValue<string>());
        Assert.AreEqual("input_image", content[1]?["type"]?.GetValue<string>());
        Assert.AreEqual("data:image/png;base64,abc123", content[1]?["image_url"]?.GetValue<string>());
        Assert.AreEqual("high", content[1]?["detail"]?.GetValue<string>());
    }

    [TestMethod]
    public void BuildAzureOpenAiChatBody_UsesCompletionTokenLimitForGpt5()
    {
        var body = LinkerAiChatService.BuildAzureOpenAiChatBody(
            new LinkerAiProviderCredential(
                "azure-openai",
                "test-key",
                "https://intrafizaifactory.services.ai.azure.com/openai/v1",
                "gpt-5.1"),
            new AzureOpenAiEndpointInfo(
                AzureOpenAiEndpointKind.OpenAiV1,
                "https://intrafizaifactory.services.ai.azure.com/openai/v1"),
            "hello",
            context: null);

        Assert.AreEqual("gpt-5.1", body["model"]?.GetValue<string>());
        Assert.IsTrue(body.ContainsKey("max_completion_tokens"));
        Assert.IsFalse(body.ContainsKey("max_tokens"));
        Assert.IsFalse(body.ContainsKey("temperature"));
    }

    [TestMethod]
    public void BuildAzureOpenAiChatBody_KeepsLegacyTokenLimitForOlderDeployments()
    {
        var body = LinkerAiChatService.BuildAzureOpenAiChatBody(
            new LinkerAiProviderCredential(
                "azure-openai",
                "test-key",
                "https://intrafizaifactory.openai.azure.com",
                "gpt-4.1-mini"),
            new AzureOpenAiEndpointInfo(
                AzureOpenAiEndpointKind.LegacyDeployment,
                "https://intrafizaifactory.openai.azure.com"),
            "hello",
            context: null);

        Assert.IsFalse(body.ContainsKey("model"));
        Assert.IsTrue(body.ContainsKey("max_tokens"));
        Assert.IsFalse(body.ContainsKey("max_completion_tokens"));
        Assert.AreEqual(0.4, body["temperature"]?.GetValue<double>());
    }
}
