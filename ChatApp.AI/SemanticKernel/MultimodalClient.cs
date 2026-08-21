using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChatApp.Core.Security;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Small provider-neutral client for OpenAI-compatible multimodal APIs.</summary>
public sealed class MultimodalClient : IMultimodalClient, IDisposable
{
    private readonly HttpClient _http;

    public MultimodalClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<string> CompleteImageAsync(
        AiSettings settings,
        MultimodalImageRequest request,
        CancellationToken ct = default)
    {
        var visionBaseUrl = settings.ResolveVisionApiBaseUrl();
        var visionApiKey = settings.ResolveVisionApiKey();
        if (string.IsNullOrWhiteSpace(visionBaseUrl) ||
            string.IsNullOrWhiteSpace(visionApiKey) ||
            string.IsNullOrWhiteSpace(settings.VisionModel))
            throw new InvalidOperationException("多模态 API 尚未完整配置。");

        var endpoint = BuildEndpoint(visionBaseUrl, settings.VisionProtocol);
        var imageDataUrl = $"data:{request.MimeType};base64,{Convert.ToBase64String(request.ImageBytes.Span)}";
        var payload = settings.VisionProtocol == MultimodalApiProtocol.Responses
            ? BuildResponsesPayload(settings.VisionModel, request.Prompt, imageDataUrl)
            : BuildChatPayload(settings.VisionModel, request.Prompt, imageDataUrl);
        var json = JsonSerializer.Serialize(payload);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.VisionTimeoutSeconds, 10, 600)));

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", visionApiKey.Trim());
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.IsSuccessStatusCode)
                return settings.VisionProtocol == MultimodalApiProtocol.Responses
                    ? ParseResponsesText(body)
                    : ParseChatText(body);

            if (attempt < 3 && IsRetryable(response.StatusCode))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt switch
                {
                    0 => 500,
                    1 => 1500,
                    _ => 3500
                }), timeout.Token);
                continue;
            }

            throw new HttpRequestException(
                $"多模态 API 返回 {(int)response.StatusCode} {response.ReasonPhrase}：{ExtractError(body)}",
                null,
                response.StatusCode);
        }
    }

    private static object BuildChatPayload(string model, string prompt, string imageDataUrl) => new
    {
        model,
        messages = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = imageDataUrl } }
                }
            }
        },
        temperature = 0.1,
        max_tokens = 800
    };

    private static object BuildResponsesPayload(string model, string prompt, string imageDataUrl) => new
    {
        model,
        input = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "input_text", text = prompt },
                    new { type = "input_image", image_url = imageDataUrl }
                }
            }
        },
        temperature = 0.1,
        max_output_tokens = 800
    };

    private static Uri BuildEndpoint(string baseUrl, MultimodalApiProtocol protocol)
    {
        var normalized = RemoteApiEndpointPolicy.NormalizeHostedApiOrThrow(baseUrl).ToString().TrimEnd('/');
        var suffix = protocol == MultimodalApiProtocol.Responses ? "/responses" : "/chat/completions";
        if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            normalized += suffix;
        return new Uri(normalized, UriKind.Absolute);
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout ||
        status == HttpStatusCode.TooManyRequests ||
        (int)status >= 500;

    internal static string ParseChatText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidDataException("多模态 API 响应缺少 choices。");
        var content = choices[0].GetProperty("message").GetProperty("content");
        var text = ReadContentText(content);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("多模态 API 返回了空内容。");
        return text;
    }

    internal static string ParseResponsesText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("output_text", out var rootOutputText) &&
            rootOutputText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rootOutputText.GetString()))
            return rootOutputText.GetString()!;
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("多模态 Responses API 响应缺少 output。");
        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var piece in content.EnumerateArray())
            {
                if (piece.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? string.Empty);
                else if (piece.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
                    parts.Add(outputText.GetString() ?? string.Empty);
            }
        }
        var result = string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidDataException("多模态 Responses API 返回了空内容。");
        return result;
    }

    private static string ReadContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;
        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                parts.Add(text.GetString() ?? string.Empty);
        }
        return string.Join("\n", parts);
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return Truncate(error.GetString());
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                    return Truncate(message.GetString());
            }
            if (doc.RootElement.TryGetProperty("message", out var rootMessage) &&
                rootMessage.ValueKind == JsonValueKind.String)
                return Truncate(rootMessage.GetString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Fall through to a sanitized/truncated raw response.
        }
        return Truncate(body);
    }

    private static string Truncate(string? value)
    {
        var text = (value ?? "未知错误").Replace('\r', ' ').Replace('\n', ' ').Trim();
        text = SecretRedaction.Redact(text);
        return text.Length <= 300 ? text : text[..300];
    }

    public void Dispose() => _http.Dispose();
}
