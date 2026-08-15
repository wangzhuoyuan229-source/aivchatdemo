namespace ChatApp.Core.Models;

/// <summary>Explicit binding between a role and a knowledge-base group.</summary>
public class RoleKnowledgeGroup
{
    public int RoleId { get; set; }

    public int KnowledgeGroupId { get; set; }
}
