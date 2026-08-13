using System;

namespace LinkScape.Tests;

[TestClass]
public sealed class AzureOpenAiEndpointTests
{
    [TestMethod]
    public void TryCreate_AcceptsLegacyAzureOpenAiResourceEndpoint()
    {
        var created = AzureOpenAiEndpoint.TryCreate(
            "https://intrafizaifactory.openai.azure.com",
            out var endpoint,
            out var error);

        Assert.IsTrue(created, error);
        Assert.AreEqual(AzureOpenAiEndpointKind.LegacyDeployment, endpoint.Kind);
        Assert.AreEqual(
            "https://intrafizaifactory.openai.azure.com/openai/models?api-version=2024-10-21",
            AzureOpenAiEndpoint.BuildModelsUrl(endpoint));
        Assert.AreEqual(
            "https://intrafizaifactory.openai.azure.com/openai/deployments/my-model/chat/completions?api-version=2024-10-21",
            AzureOpenAiEndpoint.BuildChatCompletionsUrl(endpoint, "my-model"));
        Assert.AreEqual(
            "https://intrafizaifactory.openai.azure.com/openai/responses?api-version=2024-06-01",
            AzureOpenAiEndpoint.BuildResponsesUrl(endpoint));
    }

    [TestMethod]
    public void TryCreate_AcceptsOpenAiV1EndpointWithoutDateApiVersion()
    {
        var created = AzureOpenAiEndpoint.TryCreate(
            "https://intrafizaifactory.openai.azure.com/openai/v1/",
            out var endpoint,
            out var error);

        Assert.IsTrue(created, error);
        Assert.AreEqual(AzureOpenAiEndpointKind.OpenAiV1, endpoint.Kind);
        Assert.AreEqual("https://intrafizaifactory.openai.azure.com/openai/v1/models", AzureOpenAiEndpoint.BuildModelsUrl(endpoint));
        Assert.AreEqual("https://intrafizaifactory.openai.azure.com/openai/v1/chat/completions", AzureOpenAiEndpoint.BuildChatCompletionsUrl(endpoint, "my-model"));
        Assert.AreEqual("https://intrafizaifactory.openai.azure.com/openai/v1/responses", AzureOpenAiEndpoint.BuildResponsesUrl(endpoint));
    }

    [TestMethod]
    public void TryCreate_RejectsFoundryProjectEndpointForAzureOpenAiProvider()
    {
        var created = AzureOpenAiEndpoint.TryCreate(
            "https://intrafizaifactory.services.ai.azure.com/api/projects/AIDive",
            out _,
            out var error);

        Assert.IsFalse(created);
        Assert.IsTrue(error.Contains("Foundry project endpoint", StringComparison.Ordinal));
    }
}
