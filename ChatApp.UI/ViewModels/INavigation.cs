using ChatApp.Core.Models;

namespace ChatApp.UI.ViewModels;

public interface INavigation
{
    Task OpenChatForRoleAsync(Role role);

    Task OpenConversationAsync(int conversationId);

    /// <summary>Creates a new group chat with the given members and opens it.</summary>
    Task OpenNewGroupChatAsync(IReadOnlyList<Role> members, string title);

    void Navigate(string pageKey);
}
