using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Settings;

namespace ChatApp.Tests;

public class KernelFactoryTests
{
    [Fact]
    public void DefaultsPreferDeepSeekV4Flash()
    {
        var settings = new AiSettings();

        Assert.Equal("https://api.deepseek.com/v1", settings.ApiBaseUrl);
        Assert.Equal("deepseek-v4-flash", settings.ChatModel);
        Assert.Empty(settings.EmbeddingModel);
    }

    [Fact]
    public void HostedEndpointIsNormalizedToV1()
    {
        var endpoint = KernelFactory.NormalizeEndpoint("https://api.deepseek.com");

        Assert.Equal("https://api.deepseek.com/v1", endpoint.ToString().TrimEnd('/'));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("https://localhost:8080/v1")]
    [InlineData("https://192.168.1.20/v1")]
    [InlineData("https://10.0.0.5/v1")]
    [InlineData("https://model-server.local/v1")]
    public void LocalAndPrivateEndpointsAreRejected(string endpoint)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KernelFactory.NormalizeEndpoint(endpoint));

        Assert.Contains("不支持", ex.Message);
    }

    [Fact]
    public void EmbeddingEndpointAndKeyCanUseAnIndependentHostedApi()
    {
        var settings = new AiSettings
        {
            ApiBaseUrl = "https://api.deepseek.com/v1",
            ApiKey = "deepseek-secret",
            EmbeddingApiBaseUrl = "https://embedding.example.test/openai/v1/",
            EmbeddingApiKey = "embedding-secret"
        };

        Assert.Equal(
            "https://embedding.example.test/openai/v1",
            KernelFactory.ResolveEmbeddingEndpoint(settings).ToString().TrimEnd('/'));
        Assert.Equal("embedding-secret", KernelFactory.ResolveEmbeddingApiKey(settings));
    }

    [Fact]
    public void EmbeddingInputsAreSplitIntoProviderCompatibleBatches()
    {
        var inputs = Enumerable.Range(0, 25).Select(index => $"chunk-{index}").ToArray();

        var batches = OpenAIEmbeddingService.CreateBatches(
            inputs,
            OpenAIEmbeddingService.MaxInputsPerRequest);

        Assert.Equal(3, batches.Count);
        Assert.Equal(new[] { 10, 10, 5 }, batches.Select(batch => batch.Texts.Count));
        Assert.Equal(new[] { 0, 10, 20 }, batches.Select(batch => batch.Offset));
        Assert.Equal(inputs, batches.SelectMany(batch => batch.Texts));
    }

    [Fact]
    public void LegacyLocalSettingsMigrateWithoutReusingPlaceholderKey()
    {
        var settings = new AiSettings
        {
            ApiBaseUrl = "http://127.0.0.1:8080/v1",
            ApiKey = "local",
            ChatModel = "default_model",
            EmbeddingModel = "local-embedding",
            EnableKnowledgeBase = true,
            EnableLongTermMemory = true
        };

        Assert.True(settings.MigrateToRemoteApiOnly());
        Assert.Equal(AiSettings.DefaultApiBaseUrl, settings.ApiBaseUrl);
        Assert.Equal(AiSettings.DefaultChatModel, settings.ChatModel);
        Assert.Empty(settings.ApiKey);
        Assert.Empty(settings.EmbeddingModel);
        Assert.False(settings.EnableKnowledgeBase);
        Assert.False(settings.EnableLongTermMemory);
    }

    [Theory]
    [InlineData("deepseek-chat")]
    [InlineData("deepseek-reasoner")]
    public void RetiredDeepSeekAliasesMigrateToV4Flash(string legacyModel)
    {
        var settings = new AiSettings
        {
            ApiBaseUrl = "https://api.deepseek.com/v1",
            ChatModel = legacyModel
        };

        Assert.True(settings.MigrateToRemoteApiOnly());
        Assert.Equal("deepseek-v4-flash", settings.ChatModel);
    }
}
