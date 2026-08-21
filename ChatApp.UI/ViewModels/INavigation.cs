using ChatApp.Core.Models;

namespace ChatApp.UI.ViewModels;

public interface INavigation
{
    Task OpenChatForRoleAsync(Role role);

    Task OpenConversationAsync(int conversationId);

    /// <summary>Creates a new group chat with the given members and opens it.</summary>
    Task OpenNewGroupChatAsync(IReadOnlyList<Role> members, string title, string? avatar = null);

    void Navigate(string pageKey);

    /// <summary>Navigates to the knowledge page and reveals the given document.</summary>
    Task RevealKnowledgeDocumentAsync(int documentId);

    /// <summary>Opens the memory-management window scoped to the current conversation.</summary>
    Task OpenMemoryManagementAsync();
}
