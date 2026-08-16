using System.Net;
using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;

namespace ChatApp.Tests;

public class MultimodalClientTests
{
    [Fact]
    public async Task ChatCompletionsSerializesDataUrlAndParsesText()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"description\\\":\\\"一张图\\\",\\\"tags\\\":[]}\"}}]}"));
        var client = new MultimodalClient(new HttpClient(handler));
        var settings = Settings(MultimodalApiProtocol.ChatCompletions);

        var text = await client.CompleteImageAsync(settings, Request());

        Assert.Contains("一张图", text);
        Assert.Equal("https://vision.example/v1/chat/completions", handler.LastUri!.ToString());
        Assert.Contains("\"type\":\"image_url\"", handler.LastBody);
        Assert.Contains("data:image/jpeg;base64,AQID", handler.LastBody);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
    }

    [Fact]
    public async Task ResponsesSerializesInputImageAndParsesOutputText()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"识别成功\"}]}]}"));
        var client = new MultimodalClient(new HttpClient(handler));

        var text = await client.CompleteImageAsync(Settings(MultimodalApiProtocol.Responses), Request());

        Assert.Equal("识别成功", text);
        Assert.Equal("https://vision.example/v1/responses", handler.LastUri!.ToString());
        Assert.Contains("\"type\":\"input_image\"", handler.LastBody);
    }

    [Fact]
    public async Task RetryableFailureRetriesAndEventuallySucceeds()
    {
        var handler = new RecordingHandler(call => call < 3
            ? JsonResponse("{\"error\":{\"message\":\"busy\"}}", HttpStatusCode.TooManyRequests)
            : JsonResponse("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
        var client = new MultimodalClient(new HttpClient(handler));

        Assert.Equal("ok", await client.CompleteImageAsync(Settings(MultimodalApiProtocol.ChatCompletions), Request()));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotRetry()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{}", HttpStatusCode.Unauthorized));
        var client = new MultimodalClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteImageAsync(Settings(MultimodalApiProtocol.ChatCompletions), Request()));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void ParsesResponsesOutputTextCompatibilityShape()
    {
        Assert.Equal("兼容成功", MultimodalClient.ParseResponsesText("{\"output_text\":\"兼容成功\"}"));
    }

    private static AiSettings Settings(MultimodalApiProtocol protocol) => new()
    {
        VisionApiBaseUrl = "https://vision.example/v1",
        VisionApiKey = "secret-key",
        VisionModel = "vision-model",
        VisionProtocol = protocol,
        VisionTimeoutSeconds = 30
    };

    private static MultimodalImageRequest Request() => new()
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
        public int CallCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public string? LastAuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            return responseFactory(CallCount);
        }
    }
}
