using System.Net.Http;
using ChatApp.Core.Settings;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Builds a Semantic Kernel wired to an OpenAI-compatible endpoint (BYOK).</summary>
public static class KernelFactory
{
    /// <summary>Builds a kernel with chat completion + text embedding services.</summary>
    public static Kernel Build(AiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("API Key 未配置，请先在设置中填写。");
        if (string.IsNullOrWhiteSpace(settings.ChatModel))
            throw new InvalidOperationException("聊天模型未配置。");

        // One HttpClient shared by chat + embedding; its BaseAddress points to the
        // OpenAI-compatible endpoint. SK builds its internal client from key + endpoint.
        var endpoint = NormalizeEndpoint(settings.ApiBaseUrl);
        var httpClient = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(5) };

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(settings.ChatModel, settings.ApiKey, httpClient: httpClient);
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingModel))
            builder.AddOpenAITextEmbeddingGeneration(settings.EmbeddingModel, settings.ApiKey, httpClient: httpClient);
        return builder.Build();
    }

    /// <summary>Normalizes a base URL so it ends with "/v1" (the OpenAI SDK appends its own sub-paths).</summary>
    public static Uri NormalizeEndpoint(string baseUrl)
    {
        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url)) url = "https://api.openai.com/v1";
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url += "/v1";
        return new Uri(url);
    }
}
