namespace ChatApp.Core.Settings;

/// <summary>User-configured AI service parameters (BYOK model).</summary>
public class AiSettings
{
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Number of most-recent messages kept in the short-term context window.</summary>
    public int ContextWindowSize { get; set; } = 20;

    /// <summary>Number of long-term memory fragments retrieved per turn.</summary>
    public int MemoryTopK { get; set; } = 5;

    /// <summary>Number of knowledge-base chunks retrieved per turn.</summary>
    public int KnowledgeTopK { get; set; } = 5;

    /// <summary>Trigger embedding/persistence of a memory batch once this many new messages accumulate.</summary>
    public int MemoryBatchSize { get; set; } = 50;

    public bool EnableLongTermMemory { get; set; } = true;

    public bool EnableKnowledgeBase { get; set; } = true;

    // P2
    public bool EnableVoice { get; set; } = false;

    public bool EnableAffinity { get; set; } = false;

    /// <summary>Group-chat tuning (speaker policy, max speakers, interaction style).</summary>
    public GroupChatSettings GroupChat { get; set; } = new();

    /// <summary>Approximate characters per token, used for cheap context accounting.</summary>
    public double CharsPerToken { get; set; } = 4.0;
}
