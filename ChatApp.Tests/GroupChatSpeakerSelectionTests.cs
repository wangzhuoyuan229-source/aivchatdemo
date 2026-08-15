using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;

namespace ChatApp.Tests;

public class GroupChatSpeakerSelectionTests
{
    [Fact]
    public void HybridSelectionFillsMissingSpeakerForTwoMemberGroup()
    {
        var roles = CreateRoles(1, 2);
        var members = CreateMembers(1, 2);

        var selected = GroupChatOrchestrator.CompleteHybridSelection(
            new[] { 2 }, members, roles, requestedCount: 2);

        Assert.Equal(new[] { 2, 1 }, selected);
    }

    [Fact]
    public void HybridSelectionUsesRequestedCountAndRemovesInvalidDuplicates()
    {
        var roles = CreateRoles(1, 2, 3);
        var members = CreateMembers(1, 2, 3);

        var selected = GroupChatOrchestrator.CompleteHybridSelection(
            new[] { 3, 3, 99 }, members, roles, requestedCount: 3);

        Assert.Equal(new[] { 3, 1, 2 }, selected);
    }

    [Fact]
    public void HybridSelectionNeverRequestsMoreThanAvailableMembers()
    {
        var roles = CreateRoles(1, 2);
        var members = CreateMembers(1, 2);

        var selected = GroupChatOrchestrator.CompleteHybridSelection(
            Array.Empty<int>(), members, roles, requestedCount: 20);

        Assert.Equal(new[] { 1, 2 }, selected);
    }

    private static Dictionary<int, Role> CreateRoles(params int[] ids) =>
        ids.ToDictionary(id => id, id => new Role { Id = id, Name = $"角色{id}" });

    private static List<ConversationMember> CreateMembers(params int[] ids) =>
        ids.Select((id, index) => new ConversationMember
        {
            RoleId = id,
            DisplayOrder = index
        }).ToList();
}
