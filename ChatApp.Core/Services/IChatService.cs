using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Orchestrates a chat turn: persists user message, assembles context
/// (system prompt + long-term memory + knowledge base + short-term window),
/// streams the assistant reply and persists it.
/// </summary>
public interface IChatService
{
    /// <summary>Sends user text and returns the assistant's final message.
    /// Streaming deltas are reported via <paramref name="streamingProgress"/>.</summary>
    Task<Message> SendAsync(int conversationId, string userText, IProgress<string>? streamingProgress = null, CancellationToken ct = default);

    /// <summary>
    /// Regenerates the latest assistant reply: the previous assistant message is deleted
    /// and a replacement is generated from the same preceding context without persisting
    /// a duplicate user message.
    /// </summary>
    Task<Message> RegenerateAsync(int conversationId, IProgress<string>? streamingProgress = null, CancellationToken ct = default);

    /// <summary>Produces the role's greeting for a new (or empty) conversation.</summary>
    Task<Message> GreetAsync(int conversationId, CancellationToken ct = default);
}
