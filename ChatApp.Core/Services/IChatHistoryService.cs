using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>Manages conversations and their message history (F6).</summary>
public interface IChatHistoryService
{
    Task<IReadOnlyList<Conversation>> GetConversationsAsync(int? roleId = null, CancellationToken ct = default);

    Task<Conversation> CreateConversationAsync(int roleId, string? title = null, CancellationToken ct = default);

    /// <summary>Creates a group conversation with the given member role ids (Type=Group, RoleId=null).</summary>
    Task<Conversation> CreateGroupConversationAsync(string title, IReadOnlyList<int> memberRoleIds, CancellationToken ct = default);

    Task<Conversation?> GetConversationAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Message>> GetMessagesAsync(int conversationId, int limit = 1000, CancellationToken ct = default);

    /// <summary>Returns group members ordered by <see cref="ConversationMember.DisplayOrder"/>.</summary>
    Task<IReadOnlyList<ConversationMember>> GetMembersAsync(int conversationId, CancellationToken ct = default);

    Task<Message> AddMessageAsync(Message message, CancellationToken ct = default);

    Task DeleteMessageAsync(int messageId, CancellationToken ct = default);

    Task DeleteConversationAsync(int conversationId, CancellationToken ct = default);

    /// <summary>Full-text keyword search across a conversation's messages.</summary>
    Task<IReadOnlyList<Message>> SearchAsync(string keyword, int? conversationId = null, CancellationToken ct = default);
}
