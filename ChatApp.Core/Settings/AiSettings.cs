namespace ChatApp.Core.Settings;

/// <summary>User-configured AI service parameters (BYOK model).</summary>
public class AiSettings
{
    public const string DefaultApiBaseUrl = RemoteApiEndpointPolicy.DefaultBaseUrl;

    public const string DefaultChatModel = "deepseek-v4-flash";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = DefaultChatModel;

    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>
    /// Optional OpenAI-compatible endpoint used only for embeddings. Blank means
    /// use <see cref="ApiBaseUrl"/>.
    /// </summary>
    public string EmbeddingApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional embedding-service key. Blank means use <see cref="ApiKey"/>.</summary>
    public string EmbeddingApiKey { get; set; } = string.Empty;

    /// <summary>Independent image-understanding provider used only while importing/re-describing images.</summary>
    public VisionProviderPreset VisionProviderPreset { get; set; } = VisionProviderPreset.AlibabaModelStudio;

    public MultimodalApiProtocol VisionProtocol { get; set; } = MultimodalApiProtocol.ChatCompletions;

    public string VisionApiBaseUrl { get; set; } = VisionProviderProfiles
        .Get(VisionProviderPreset.AlibabaModelStudio).baseUrl;

    public string VisionApiKey { get; set; } = string.Empty;

    public string VisionModel { get; set; } = VisionProviderProfiles
        .Get(VisionProviderPreset.AlibabaModelStudio).model;

    public int VisionTimeoutSeconds { get; set; } = 90;

    public int VisionMaxConcurrency { get; set; } = 3;

    /// <summary>Number of most-recent messages kept in the short-term context window.</summary>
    public int ContextWindowSize { get; set; } = 20;

    /// <summary>Number of long-term memory fragments retrieved per turn.</summary>
    public int MemoryTopK { get; set; } = 5;

    /// <summary>Number of knowledge-base chunks retrieved per turn.</summary>
    public int KnowledgeTopK { get; set; } = 5;

    /// <summary>Minimum cosine similarity accepted for a knowledge hit.</summary>
    public double KnowledgeMinScore { get; set; } = 0.35;

    public int KnowledgeImageTopK { get; set; } = 5;

    public double KnowledgeImageMinScore { get; set; } = 0.35;

    /// <summary>Maximum characters injected from retrieved knowledge per turn.</summary>
    public int KnowledgeContextCharBudget { get; set; } = 6000;

    /// <summary>Number of adjacent chunks included on either side of a direct hit.</summary>
    public int KnowledgeNeighborRadius { get; set; } = 1;

    /// <summary>Trigger embedding/persistence of a memory batch once this many new messages accumulate.</summary>
    public int MemoryBatchSize { get; set; } = 50;

    public bool EnableLongTermMemory { get; set; } = false;

    public bool EnableKnowledgeBase { get; set; } = false;

    /// <summary>Sampling temperature used for private and group role replies.</summary>
    public double ChatTemperature { get; set; } = 0.65;

    // P2
    public bool EnableVoice { get; set; } = false;

    public bool EnableAffinity { get; set; } = false;

    /// <summary>Group-chat tuning (speaker policy, max speakers, interaction style).</summary>
    public GroupChatSettings GroupChat { get; set; } = new();

    /// <summary>Approximate characters per token, used for cheap context accounting.</summary>
    public double CharsPerToken { get; set; } = 4.0;

    /// <summary>
    /// Migrates legacy local-model settings without ever forwarding their placeholder key
    /// to a hosted provider. Returns true when any value was changed.
    /// </summary>
    public bool MigrateToRemoteApiOnly()
    {
        var changed = false;
        if (!RemoteApiEndpointPolicy.TryNormalize(ApiBaseUrl, out var chatEndpoint, out _))
        {
            ApiBaseUrl = DefaultApiBaseUrl;
            ApiKey = string.Empty;
            ChatModel = DefaultChatModel;
            EmbeddingModel = string.Empty;
            EmbeddingApiBaseUrl = string.Empty;
            EmbeddingApiKey = string.Empty;
            EnableLongTermMemory = false;
            EnableKnowledgeBase = false;
            changed = true;
        }
        else
        {
            var normalized = chatEndpoint.ToString().TrimEnd('/');
            if (!string.Equals(ApiBaseUrl, normalized, StringComparison.Ordinal))
            {
                ApiBaseUrl = normalized;
                changed = true;
            }
        }

        if (Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var chatUri) &&
            chatUri.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase) &&
            ChatModel is "deepseek-chat" or "deepseek-reasoner" or "default_model")
        {
            ChatModel = DefaultChatModel;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl))
        {
            if (RemoteApiEndpointPolicy.TryNormalize(EmbeddingApiBaseUrl, out var embeddingEndpoint, out _))
            {
                var normalized = embeddingEndpoint.ToString().TrimEnd('/');
                if (!string.Equals(EmbeddingApiBaseUrl, normalized, StringComparison.Ordinal))
                {
                    EmbeddingApiBaseUrl = normalized;
                    changed = true;
                }
            }
            else
            {
                EmbeddingApiBaseUrl = string.Empty;
                EmbeddingApiKey = string.Empty;
                EmbeddingModel = string.Empty;
                EnableLongTermMemory = false;
                EnableKnowledgeBase = false;
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(VisionApiBaseUrl))
        {
            if (RemoteApiEndpointPolicy.TryNormalizeHostedApi(VisionApiBaseUrl, out var visionEndpoint, out _))
            {
                var normalized = visionEndpoint.ToString().TrimEnd('/');
                if (!string.Equals(VisionApiBaseUrl, normalized, StringComparison.Ordinal))
                {
                    VisionApiBaseUrl = normalized;
                    changed = true;
                }
            }
            else
            {
                VisionApiBaseUrl = string.Empty;
                VisionApiKey = string.Empty;
                VisionModel = string.Empty;
                changed = true;
            }
        }

        return changed;
    }
}
