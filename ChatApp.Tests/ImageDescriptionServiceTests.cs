using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace ChatApp.Tests;

public class ImageDescriptionServiceTests
{
    [Fact]
    public void StrictJsonParserAcceptsFenceAndStringTags()
    {
        var result = ImageDescriptionService.ParseDescription(
            "```json\n{\"description\":\"港口夜景\",\"tags\":\"港口，夜景,月光\"}\n```");

        Assert.Equal("港口夜景", result.description);
        Assert.Equal("港口,夜景,月光", result.tags);
    }

    [Fact]
    public void StrictJsonParserRejectsMissingDescription()
    {
        Assert.Throws<InvalidDataException>(() =>
            ImageDescriptionService.ParseDescription("{\"tags\":[\"图\"]}"));
    }

    [Fact]
    public void FaceRegionParserNormalizesModelCoordinates()
    {
        var region = ImageDescriptionService.ParseFaceRegion(
            "```json\n{\"found\":true,\"box\":[325,210,610,590]}\n```");

        Assert.NotNull(region);
        Assert.Equal(0.325, region!.Left, 3);
        Assert.Equal(0.21, region.Top, 3);
        Assert.Equal(0.61, region.Right, 3);
        Assert.Equal(0.59, region.Bottom, 3);
    }

    [Fact]
    public void FaceRegionParserAcceptsNoFaceResponse()
    {
        Assert.Null(ImageDescriptionService.ParseFaceRegion("{\"found\":false,\"box\":[]}"));
    }

    [Fact]
    public void NormalizationLimitsLongEdgeAndDoesNotModifyOriginal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatapp-image-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new SKBitmap(3000, 300, SKColorType.Rgba8888, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Color = SKColors.Red };
                canvas.DrawCircle(1500, 150, 100, paint);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                encoded.SaveTo(output);
            }
            var original = File.ReadAllBytes(path);

            var normalized = ImageDescriptionService.NormalizeToJpeg(path);

            Assert.True(normalized.Length <= 6 * 1024 * 1024);
            Assert.Equal((byte)0xFF, normalized[0]);
            Assert.Equal((byte)0xD8, normalized[1]);
            using var memory = new SKMemoryStream(normalized);
            using var codec = SKCodec.Create(memory);
            Assert.NotNull(codec);
            Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= 2048);
            using var decoded = SKBitmap.Decode(normalized);
            var corner = decoded.GetPixel(0, 0);
            Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RoleAvatarSnapshotIsCompactSquareJpeg()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatapp-avatar-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new SKBitmap(900, 500))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.CornflowerBlue);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                encoded.SaveTo(output);
            }

            var bytes = KnowledgeService.CreateSquareAvatarJpeg(path);

            Assert.True(bytes.Length < 256 * 1024);
            Assert.Equal((byte)0xFF, bytes[0]);
            Assert.Equal((byte)0xD8, bytes[1]);
            using var decoded = SKBitmap.Decode(bytes);
            Assert.Equal(256, decoded.Width);
            Assert.Equal(256, decoded.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RoleAvatarCropZoomsAroundDetectedFace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatapp-face-avatar-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new SKBitmap(800, 800))
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = SKColors.Red })
            {
                canvas.Clear(SKColors.White);
                canvas.DrawRect(new SKRect(320, 240, 480, 440), paint);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                encoded.SaveTo(output);
            }

            var bytes = KnowledgeService.CreateSquareAvatarJpeg(path, faceRegion: new ImageFaceRegion
            {
                Left = 0.4,
                Top = 0.3,
                Right = 0.6,
                Bottom = 0.55
            });

            using var decoded = SKBitmap.Decode(bytes);
            var redPixels = 0;
            for (var y = 0; y < decoded.Height; y += 4)
            for (var x = 0; x < decoded.Width; x += 4)
            {
                var pixel = decoded.GetPixel(x, y);
                if (pixel.Red > 180 && pixel.Green < 100 && pixel.Blue < 100) redPixels++;
            }
            Assert.True(redPixels > 500, $"Expected a zoomed face crop, but only sampled {redPixels} red pixels.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ConnectionTestClassifiesAuthenticationFailure()
    {
        var service = new ImageDescriptionService(
            new FixedConfigurationService(new AiSettings
            {
                UseUnifiedApi = false,
                VisionApiBaseUrl = "https://vision.example/v1",
                VisionApiKey = "bad-key",
                VisionModel = "vision"
            }),
            new ThrowingMultimodalClient(new HttpRequestException(
                "unauthorized", null, System.Net.HttpStatusCode.Unauthorized)),
            NullLogger<ImageDescriptionService>.Instance);

        var result = await service.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("authentication", result.ErrorCategory);
        Assert.Contains("鉴权失败", result.ErrorDetail);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest, "model not found", "model_or_endpoint")]
    [InlineData(System.Net.HttpStatusCode.BadRequest, "image input is not supported", "unsupported_image")]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, "rate limit", "rate_limited")]
    public async Task ConnectionTestClassifiesCommonProviderErrors(
        System.Net.HttpStatusCode status,
        string detail,
        string expectedCategory)
    {
        var service = new ImageDescriptionService(
            new FixedConfigurationService(new AiSettings
            {
                UseUnifiedApi = false,
                VisionApiBaseUrl = "https://vision.example/v1",
                VisionApiKey = "key",
                VisionModel = "vision"
            }),
            new ThrowingMultimodalClient(new HttpRequestException(detail, null, status)),
            NullLogger<ImageDescriptionService>.Instance);

        var result = await service.TestConnectionAsync();

        Assert.Equal(expectedCategory, result.ErrorCategory);
    }

    private sealed class FixedConfigurationService(AiSettings settings) : IConfigurationService
    {
        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AiSettings value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class ThrowingMultimodalClient(Exception exception) : IMultimodalClient
    {
        public Task<string> CompleteImageAsync(AiSettings settings, MultimodalImageRequest request, CancellationToken ct = default) =>
            Task.FromException<string>(exception);
    }
}
