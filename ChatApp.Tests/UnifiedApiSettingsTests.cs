using System.Net;
using System.Text.Json;
using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public class UnifiedApiSettingsTests
{
    [Fact]
    public void UnifiedModeRoutesAllServicesToMainEndpointAndKey()
    {
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = "https://api.siliconflow.cn/v1",
            ApiKey = "main-key",
            EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            EmbeddingApiKey = "embed-key",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            VisionApiKey = "vision-key"
        };

        Assert.Equal("https://api.siliconflow.cn/v1", settings.ResolveEmbeddingApiBaseUrl());
        Assert.Equal("main-key", settings.ResolveEmbeddingApiKey());
        Assert.Equal("https://api.siliconflow.cn/v1", settings.ResolveVisionApiBaseUrl());
        Assert.Equal("main-key", settings.ResolveVisionApiKey());
    }

    [Fact]
    public void IndependentModeKeepsSeparateEndpointsAndKeys()
    {
        var settings = new AiSettings
        {
            ApiBaseUrl = "https://api.deepseek.com/v1",
            ApiKey = "chat-key",
            EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            EmbeddingApiKey = "embed-key",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            VisionApiKey = "vision-key"
        };

        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", settings.ResolveEmbeddingApiBaseUrl());
        Assert.Equal("embed-key", settings.ResolveEmbeddingApiKey());
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4", settings.ResolveVisionApiBaseUrl());
        Assert.Equal("vision-key", settings.ResolveVisionApiKey());
    }

    [Fact]
    public void BlankEmbeddingOverridesStillFallBackToChatApi()
    {
        var settings = new AiSettings
        {
            ApiBaseUrl = "https://api.deepseek.com/v1",
            ApiKey = "chat-key"
        };

        Assert.Equal("https://api.deepseek.com/v1", settings.ResolveEmbeddingApiBaseUrl());
        Assert.Equal("chat-key", settings.ResolveEmbeddingApiKey());
    }

    [Fact]
    public void UnifiedModeEmbeddingResolvesToChatEndpointInKernelFactory()
    {
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = "https://api.siliconflow.cn/v1",
            ApiKey = "main-key",
            EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            EmbeddingApiKey = "embed-key"
        };

        Assert.Equal(
            "https://api.siliconflow.cn/v1",
            KernelFactory.ResolveEmbeddingEndpoint(settings).ToString().TrimEnd('/'));
        Assert.Equal("main-key", KernelFactory.ResolveEmbeddingApiKey(settings));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("https://localhost:8080/v1")]
    [InlineData("https://192.168.1.20/v1")]
    public void UnifiedModeStillRejectsLocalEndpointsAtRuntime(string baseUrl)
    {
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = baseUrl,
            ApiKey = "key",
            EmbeddingModel = "embed"
        };

        Assert.Throws<InvalidOperationException>(() => KernelFactory.ResolveEmbeddingEndpoint(settings));
    }

    [Fact]
    public async Task UnifiedModeVisionRequestStillRejectsLocalEndpoints()
    {
        var client = new MultimodalClient(new HttpClient(new RecordingHandler(_ => JsonResponse("{}"))));
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = "https://192.168.1.20/v1",
            ApiKey = "key",
            VisionModel = "vision-model",
            VisionTimeoutSeconds = 30
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteImageAsync(settings, VisionRequest()));
    }

    [Fact]
    public async Task UnifiedModeSendsVisionRequestsToMainEndpointWithMainKey()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
        var client = new MultimodalClient(new HttpClient(handler));
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = "https://api.siliconflow.cn/v1",
            ApiKey = "main-key",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            VisionApiKey = "vision-key",
            VisionModel = "Qwen/Qwen2.5-VL-72B-Instruct",
            VisionProtocol = MultimodalApiProtocol.ChatCompletions,
            VisionTimeoutSeconds = 30
        };

        var text = await client.CompleteImageAsync(settings, VisionRequest());

        Assert.Equal("ok", text);
        Assert.Equal("https://api.siliconflow.cn/v1/chat/completions", handler.LastUri!.ToString());
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
        Assert.Equal("main-key", handler.LastAuthorizationParameter);
    }

    [Fact]
    public async Task IndependentModeVisionRequestKeepsDedicatedEndpointAndKey()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
        var client = new MultimodalClient(new HttpClient(handler));
        var settings = new AiSettings
        {
            ApiBaseUrl = "https://api.deepseek.com/v1",
            ApiKey = "chat-key",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            VisionApiKey = "vision-key",
            VisionModel = "glm-4.6v-flash",
            VisionProtocol = MultimodalApiProtocol.ChatCompletions,
            VisionTimeoutSeconds = 30
        };

        var text = await client.CompleteImageAsync(settings, VisionRequest());

        Assert.Equal("ok", text);
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4/chat/completions", handler.LastUri!.ToString());
        Assert.Equal("vision-key", handler.LastAuthorizationParameter);
    }

    [Fact]
    public async Task UnifiedModeConnectionTestPassesConfiguredGateWithMainKeyOnly()
    {
        var service = new ImageDescriptionService(
            new FixedConfigurationService(new AiSettings
            {
                UseUnifiedApi = true,
                ApiBaseUrl = "https://api.siliconflow.cn/v1",
                ApiKey = "main-key",
                VisionModel = "vision-model"
            }),
            new ThrowingMultimodalClient(new HttpRequestException(
                "unauthorized", null, HttpStatusCode.Unauthorized)),
            NullLogger<ImageDescriptionService>.Instance);

        var result = await service.TestConnectionAsync();

        Assert.NotEqual("not_configured", result.ErrorCategory);
        Assert.Equal("authentication", result.ErrorCategory);
        Assert.Equal("统一 API（主端点）", result.Provider);
    }

    [Fact]
    public async Task UnifiedModeWithoutMainKeyReportsNotConfigured()
    {
        var service = new ImageDescriptionService(
            new FixedConfigurationService(new AiSettings
            {
                UseUnifiedApi = true,
                ApiBaseUrl = "https://api.siliconflow.cn/v1",
                VisionModel = "vision-model"
            }),
            new ThrowingMultimodalClient(new HttpRequestException(
                "unauthorized", null, HttpStatusCode.Unauthorized)),
            NullLogger<ImageDescriptionService>.Instance);

        var result = await service.TestConnectionAsync();

        Assert.Equal("not_configured", result.ErrorCategory);
        Assert.Contains("统一 API", result.ErrorDetail);
    }

    [Fact]
    public void LegacySettingsJsonWithoutUnifiedFlagDefaultsToIndependentMode()
    {
        var json = "{\"apiBaseUrl\":\"https://api.deepseek.com/v1\",\"apiKey\":\"chat-key\"," +
                   "\"chatModel\":\"deepseek-v4-flash\"," +
                   "\"visionApiBaseUrl\":\"https://dashscope.aliyuncs.com/compatible-mode/v1\"}";

        var settings = JsonSerializer.Deserialize<AiSettings>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.False(settings.UseUnifiedApi);
        Assert.Equal("chat-key", settings.ResolveEmbeddingApiKey());
        Assert.Equal(string.Empty, settings.ResolveVisionApiKey());
    }

    [Fact]
    public void MigrationNormalizesEndpointsButKeepsUnifiedMode()
    {
        var settings = new AiSettings
        {
            UseUnifiedApi = true,
            ApiBaseUrl = "https://api.siliconflow.cn/v1/",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4/"
        };

        Assert.True(settings.MigrateToRemoteApiOnly());
        Assert.True(settings.UseUnifiedApi);
        Assert.Equal("https://api.siliconflow.cn/v1", settings.ApiBaseUrl);
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4", settings.VisionApiBaseUrl);
    }

    [Fact]
    public async Task TogglingUnifiedModePersistsFlagAndKeepsIndependentConfiguration()
    {
        var config = new RecordingConfigurationService(new AiSettings
        {
            ApiBaseUrl = "https://api.siliconflow.cn/v1",
            ApiKey = "main-key",
            ChatModel = "deepseek-ai/DeepSeek-V3",
            EmbeddingModel = "BAAI/bge-m3",
            EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            EmbeddingApiKey = "embed-key",
            VisionApiBaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            VisionApiKey = "vision-key",
            VisionModel = "glm-4.6v-flash"
        });
        var viewModel = new SettingsViewModel(config, new MemoryUiSettingsService());
        await viewModel.LoadAsync();

        viewModel.UseUnifiedApi = true;

        var saved = await config.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(saved.UseUnifiedApi);
        // The stored independent configuration survives so the user can switch back.
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", saved.EmbeddingApiBaseUrl);
        Assert.Equal("embed-key", saved.EmbeddingApiKey);
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4", saved.VisionApiBaseUrl);
        Assert.Equal("vision-key", saved.VisionApiKey);
        // Effective runtime resolution goes through the main endpoint and key.
        Assert.Equal("https://api.siliconflow.cn/v1", saved.ResolveEmbeddingApiBaseUrl());
        Assert.Equal("main-key", saved.ResolveVisionApiKey());
    }

    [Fact]
    public async Task UnifiedModeWithDeepSeekEndpointStillWarnsAboutEmbedding()
    {
        var config = new RecordingConfigurationService(new AiSettings());
        var viewModel = new SettingsViewModel(config, new MemoryUiSettingsService());
        await viewModel.LoadAsync();

        viewModel.ApiKey = "sk-test";
        viewModel.EnableKnowledgeBase = true;
        viewModel.UseUnifiedApi = true;

        var saved = await config.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(saved.UseUnifiedApi);
        Assert.Contains("Embedding", viewModel.StatusText);
    }

    private static MultimodalImageRequest VisionRequest() => new()
    {
        ImageBytes = new byte[] { 1, 2, 3 },
        MimeType = "image/jpeg",
        Prompt = "describe"
    };

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            _ = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(1);
        }
    }

    private sealed class FixedConfigurationService(AiSettings settings) : IConfigurationService
    {
        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AiSettings value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class ThrowingMultimodalClient(Exception exception) : IMultimodalClient
    {
        public Task<string> CompleteImageAsync(
            AiSettings settings,
            MultimodalImageRequest request,
            CancellationToken ct = default) =>
            Task.FromException<string>(exception);
    }

    private sealed class RecordingConfigurationService(AiSettings initial) : IConfigurationService
    {
        private AiSettings _settings = initial;
        public TaskCompletionSource<AiSettings> Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);

        public Task SaveAsync(AiSettings settings, CancellationToken ct = default)
        {
            _settings = settings;
            Saved.TrySetResult(settings);
            return Task.CompletedTask;
        }

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class MemoryUiSettingsService : IUiSettingsService
    {
        private UiSettings _settings = new();
        public Task<UiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);
        public Task SaveAsync(UiSettings settings, CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }
}
