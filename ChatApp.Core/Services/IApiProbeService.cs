namespace ChatApp.Core.Services;

/// <summary>
/// Outcome of a minimal remote-API connectivity probe (plan 3.4).
/// Messages are user-facing and must never contain API keys or tokens.
/// </summary>
public sealed class ConnectionProbeResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ConnectionProbeResult Ok(string message) => new() { IsSuccess = true, Message = message };
    public static ConnectionProbeResult Fail(string message) => new() { IsSuccess = false, Message = message };
}

/// <summary>Probes the chat and embedding endpoints with minimal requests.</summary>
public interface IApiProbeService
{
    Task<ConnectionProbeResult> TestChatAsync(CancellationToken ct = default);
    Task<ConnectionProbeResult> TestEmbeddingAsync(CancellationToken ct = default);
}