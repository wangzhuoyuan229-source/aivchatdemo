using ChatApp.Core.Models;
using ChatApp.Core.Settings;

namespace ChatApp.Core.Services;

public sealed class MultimodalImageRequest
{
    public ReadOnlyMemory<byte> ImageBytes { get; init; }

    public string MimeType { get; init; } = "image/jpeg";

    public string Prompt { get; init; } = string.Empty;
}

public sealed record ImageDescriptionResult
{
    public string Description { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public ImageDescriptionSource Source { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? Detail { get; init; }
}

/// <summary>Normalized primary-face bounds relative to the oriented source image.</summary>
public sealed record ImageFaceRegion
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Right { get; init; }

    public double Bottom { get; init; }
}

public sealed record VisionConnectionTestResult
{
    public bool IsSuccess { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string ErrorCategory { get; init; } = string.Empty;

    public string ErrorDetail { get; init; } = string.Empty;
}

public interface IMultimodalClient
{
    Task<string> CompleteImageAsync(AiSettings settings, MultimodalImageRequest request, CancellationToken ct = default);
}

public interface IImageDescriptionService
{
    Task<ImageDescriptionResult> DescribeAsync(
        string imagePath,
        string fileName,
        string sourceRelativePath,
        CancellationToken ct = default);

    /// <summary>Returns null when vision is unavailable or no reliable human-like face is visible.</summary>
    Task<ImageFaceRegion?> LocatePrimaryFaceAsync(
        string imagePath,
        string subjectHint,
        CancellationToken ct = default);

    Task<VisionConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}
