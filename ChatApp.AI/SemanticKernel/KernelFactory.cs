using System.Net.Http;
using ChatApp.Core.Settings;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Builds a Semantic Kernel wired to an OpenAI-compatible endpoint (BYOK).</summary>
public static class KernelFactory
{
/// <summary>
    /// Builds a kernel with chat completion + text embedding services. Pass a short
    /// <paramref name="requestTimeout"/> for connectivity probes; null keeps the
    /// default five-minute streaming timeout.
    /// </summary>
    public static Kernel Build(AiSettings settings, TimeSpan? requestTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("API Key 未配置，请先在设置中填写。");
        if (string.IsNullOrWhiteSpace(settings.ChatModel))
            throw new InvalidOperationException("聊天模型未配置。");

        // Defense in depth: legacy databases or callers outside the settings UI
        // must never re-enable local inference by supplying a loopback endpoint.
        var chatClient = CreateHttpClient(settings.ApiBaseUrl, requestTimeout);

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(settings.ChatModel, settings.ApiKey, httpClient: chatClient);
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingModel))
        {
            var embeddingApiKey = ResolveEmbeddingApiKey(settings);
            var embeddingClient = CreateHttpClient(ResolveEmbeddingEndpoint(settings).ToString(), requestTimeout);
            builder.AddOpenAIEmbeddingGenerator(
                settings.EmbeddingModel,
                embeddingApiKey,
                httpClient: embeddingClient);
        }
        return builder.Build();
    }

    internal static Uri ResolveEmbeddingEndpoint(AiSettings settings) => NormalizeEndpoint(
        settings.ResolveEmbeddingApiBaseUrl());

    internal static string ResolveEmbeddingApiKey(AiSettings settings) =>
        settings.ResolveEmbeddingApiKey();

private static HttpClient CreateHttpClient(string baseUrl, TimeSpan? timeout = null) => new()
    {
        BaseAddress = NormalizeEndpoint(baseUrl),
        Timeout = timeout ?? TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// Validates a hosted HTTPS endpoint and ensures it ends with "/v1"
    /// (the OpenAI SDK appends its own sub-paths).
    /// </summary>
    public static Uri NormalizeEndpoint(string baseUrl)
        => RemoteApiEndpointPolicy.NormalizeOrThrow(baseUrl);
}
