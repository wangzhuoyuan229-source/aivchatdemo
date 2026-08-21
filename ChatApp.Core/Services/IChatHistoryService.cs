using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>Manages conversations and their message history (F6).</summary>
public interface IChatHistoryService
{
    Task<IReadOnlyList<Conversation>> GetConversationsAsync(int? roleId = null, CancellationToken ct = default);

    Task<Conversation> CreateConversationAsync(int roleId, string? title = null, CancellationToken ct = default);

    /// <summary>Creates a group conversation with the given member role ids (Type=Group, RoleId=null).</summary>
    Task<Conversation> CreateGroupConversationAsync(string title, IReadOnlyList<int> memberRoleIds,
        string? avatar = null, CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> messages in ascending order. When
    /// <paramref name="beforeId"/> is set, only messages older than that id are
    /// returned (cursor pagination for the "load earlier" window).
    /// </summary>
    Task<IReadOnlyList<Message>> GetMessagesAsync(int conversationId, int limit = 1000, CancellationToken ct = default, int? beforeId = null);

    /// <summary>Returns group members ordered by <see cref="ConversationMember.DisplayOrder"/>.</summary>
    Task<IReadOnlyList<ConversationMember>> GetMembersAsync(int conversationId, CancellationToken ct = default);

    Task<Message> AddMessageAsync(Message message, CancellationToken ct = default);

    Task DeleteMessageAsync(int messageId, CancellationToken ct = default);

    /// <summary>Deletes the given message and every later message in the same conversation.</summary>
    Task<int> DeleteMessagesFromAsync(int conversationId, int messageIdInclusive, CancellationToken ct = default);

    /// <summary>Renames a conversation title.</summary>
    Task RenameConversationAsync(int conversationId, string title, CancellationToken ct = default);

    /// <summary>Pins or unpins a conversation (pinned ones sort first).</summary>
    Task SetConversationPinnedAsync(int conversationId, bool pinned, CancellationToken ct = default);

    /// <summary>Deletes a conversation and its messages, attachments, group members and derived memories.</summary>
    Task DeleteConversationAsync(int conversationId, CancellationToken ct = default);

    /// <summary>Full-text keyword search across a conversation's messages.</summary>
    Task<IReadOnlyList<Message>> SearchAsync(string keyword, int? conversationId = null, CancellationToken ct = default);
}
