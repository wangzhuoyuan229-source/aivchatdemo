using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Orchestrates a group-chat turn: persists the user message, picks speakers
/// (round-robin or director-driven hybrid), streams each speaker's reply and
/// persists it. Streaming events are reported via <paramref name="progress"/>.
/// </summary>
public interface IGroupChatService
{
    /// <summary>Returns all assistant messages produced this turn.</summary>
    Task<IReadOnlyList<Message>> SendAsync(
        int conversationId,
        string userText,
        IProgress<GroupChatEvent>? progress = null,
        CancellationToken ct = default);
}
