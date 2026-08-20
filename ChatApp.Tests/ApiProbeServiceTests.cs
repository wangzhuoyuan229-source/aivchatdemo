using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

/// <summary>
/// Coverage for the plan-3.4 connectivity probes. Network paths cannot run in
/// tests, so the guard clauses (missing key / model) and the guarantee that
/// failure messages never contain secrets are verified locally.
/// </summary>
public class ApiProbeServiceTests
{
    [Fact]
    public async Task ChatProbeFailsFastWithoutApiKey()
    {
        var probe = new ApiProbeService(
            new FixedConfigurationService(new AiSettings { ApiKey = "" }),
            NullLogger<ApiProbeService>.Instance);

        var result = await probe.TestChatAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("API Key", result.Message);
    }

    [Fact]
    public async Task ChatProbeFailsFastWithoutChatModel()
    {
        var probe = new ApiProbeService(
            new FixedConfigurationService(new AiSettings { ApiKey = "sk-test", ChatModel = "" }),
            NullLogger<ApiProbeService>.Instance);

        var result = await probe.TestChatAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("聊天模型", result.Message);
    }

    [Fact]
    public async Task EmbeddingProbeFailsFastWithoutEmbeddingModel()
    {
        var probe = new ApiProbeService(
            new FixedConfigurationService(new AiSettings { ApiKey = "sk-test", ChatModel = "deepseek-v4-flash" }),
            NullLogger<ApiProbeService>.Instance);

        var result = await probe.TestEmbeddingAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("Embedding", result.Message);
    }

    [Fact]
    public async Task FailureMessagesNeverContainTheApiKey()
    {
        const string key = "sk-leaked-key-42";
        var probe = new ApiProbeService(
            new FixedConfigurationService(new AiSettings { ApiKey = key }),
            NullLogger<ApiProbeService>.Instance);

        var chat = await probe.TestChatAsync();
        var embedding = await probe.TestEmbeddingAsync();

        Assert.DoesNotContain(key, chat.Message);
        Assert.DoesNotContain(key, embedding.Message);
    }

    private sealed class FixedConfigurationService(AiSettings settings) : IConfigurationService
    {
        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AiSettings value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}