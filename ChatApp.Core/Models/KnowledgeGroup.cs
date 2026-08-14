namespace ChatApp.Core.Models;

/// <summary>A user-defined group used to organize knowledge documents.</summary>
public class KnowledgeGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
