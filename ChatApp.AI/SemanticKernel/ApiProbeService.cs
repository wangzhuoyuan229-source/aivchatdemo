using ChatApp.Core.Security;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;

namespace ChatApp.AI.SemanticKernel;

/// <summary>
/// Minimal connectivity probes for the settings page (plan 3.4). Each probe sends
/// one tiny request through the configured remote endpoint with a short timeout
/// and classifies failures without ever echoing the API key.
/// </summary>
public sealed class ApiProbeService : IApiProbeService
{
    private const int ProbeTimeoutSeconds = 20;
    private readonly IConfigurationService _config;
    private readonly ILogger<ApiProbeService> _logger;

    public ApiProbeService(IConfigurationService config, ILogger<ApiProbeService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ConnectionProbeResult> TestChatAsync(CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return ConnectionProbeResult.Fail("未填写 API Key。");
        if (string.IsNullOrWhiteSpace(settings.ChatModel)) return ConnectionProbeResult.Fail("未填写聊天模型。");
        try
        {
            var kernel = KernelFactory.Build(settings, TimeSpan.FromSeconds(ProbeTimeoutSeconds));
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage("请只回复两个字：你好");
            var reply = await chat.GetChatMessageContentAsync(
                history, new OpenAIPromptExecutionSettings { Temperature = 0 }, kernel, ct);
            var text = reply.Content?.Trim() ?? string.Empty;
            return text.Length > 0
                ? ConnectionProbeResult.Ok($"连接成功：{settings.ChatModel} 已响应")
                : ConnectionProbeResult.Fail("连接成功但模型返回了空响应。");
        }
        catch (HttpRequestException ex) { return ConnectionProbeResult.Fail(ClassifyHttpError(ex)); }
        catch (TaskCanceledException) { return ConnectionProbeResult.Fail($"连接超时（{ProbeTimeoutSeconds} 秒内无响应）。"); }
        catch (Exception ex) { return ConnectionProbeResult.Fail(SafeMessage(ex)); }
    }

    public async Task<ConnectionProbeResult> TestEmbeddingAsync(CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return ConnectionProbeResult.Fail("未填写 API Key。");
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
            return ConnectionProbeResult.Fail("未填写 Embedding 模型（统一模式下若主端点不提供 Embedding，请改用三路独立模式）。");
        try
        {
            var kernel = KernelFactory.Build(settings, TimeSpan.FromSeconds(ProbeTimeoutSeconds));
            var embedding = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var vector = await embedding.GenerateEmbeddingAsync("连接测试", kernel, ct);
            return vector.Length > 0
                ? ConnectionProbeResult.Ok($"连接成功：{settings.EmbeddingModel} 返回 {vector.Length} 维向量")
                : ConnectionProbeResult.Fail("连接成功但返回了空向量。");
        }
        catch (HttpRequestException ex) { return ConnectionProbeResult.Fail(ClassifyHttpError(ex)); }
        catch (TaskCanceledException) { return ConnectionProbeResult.Fail($"连接超时（{ProbeTimeoutSeconds} 秒内无响应）。"); }
        catch (Exception ex) { return ConnectionProbeResult.Fail(SafeMessage(ex)); }
    }

    private static string ClassifyHttpError(HttpRequestException ex) => ex.StatusCode switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "鉴权失败：API Key 无效或没有权限。",
        System.Net.HttpStatusCode.NotFound => "端点或模型不存在：请检查 API Base URL 与模型 ID。",
        System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁：请稍后再试或检查套餐额度。",
        System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity => "请求被拒绝：请检查模型 ID 与请求格式。",
        _ when ex.StatusCode is not null && (int)ex.StatusCode >= 500 => "服务端暂时不可用，请稍后再试。",
        _ => "网络请求失败：请检查网络连接与端点地址。"
    };

    private static string SafeMessage(Exception ex)
    {
        _ = ex;
        // Never surface raw exception text: providers may echo the request (and key).
        return "连接失败：请求未能完成，请检查端点地址、模型与网络。";
    }
}