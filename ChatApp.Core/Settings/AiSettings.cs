namespace ChatApp.Core.Settings;

/// <summary>User-configured AI service parameters (BYOK model).</summary>
public class AiSettings
{
    public const string DefaultApiBaseUrl = RemoteApiEndpointPolicy.DefaultBaseUrl;

    public const string DefaultChatModel = "deepseek-ai/DeepSeek-V3.1";

    public const string DefaultEmbeddingModel = "BAAI/bge-m3";

    public const string DefaultVisionModel = "Qwen/Qwen3-VL-32B-Instruct";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = DefaultChatModel;

    /// <summary>
    /// When true, chat, embeddings and vision all share <see cref="ApiBaseUrl"/> and
    /// <see cref="ApiKey"/> (single-provider setups such as SiliconFlow). The three
    /// services keep independent model IDs. When false, each service may use its own
    /// endpoint and key as before.
    /// </summary>
    public bool UseUnifiedApi { get; set; } = false;

    public UnifiedApiPreset UnifiedPreset { get; set; } = UnifiedApiPreset.SiliconFlow;

    public string EmbeddingModel { get; set; } = DefaultEmbeddingModel;

    /// <summary>
    /// Optional OpenAI-compatible endpoint used only for embeddings. Blank means
    /// use <see cref="ApiBaseUrl"/>.
    /// </summary>
    public string EmbeddingApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional embedding-service key. Blank means use <see cref="ApiKey"/>.</summary>
    public string EmbeddingApiKey { get; set; } = string.Empty;

    /// <summary>Independent image-understanding provider used only while importing/re-describing images.</summary>
    public VisionProviderPreset VisionProviderPreset { get; set; } = VisionProviderPreset.SiliconFlow;

    public MultimodalApiProtocol VisionProtocol { get; set; } = VisionProviderProfiles
        .Get(VisionProviderPreset.SiliconFlow).protocol;

    public string VisionApiBaseUrl { get; set; } = VisionProviderProfiles
        .Get(VisionProviderPreset.SiliconFlow).baseUrl;

    public string VisionApiKey { get; set; } = string.Empty;

    public string VisionModel { get; set; } = VisionProviderProfiles
        .Get(VisionProviderPreset.SiliconFlow).model;

    public int VisionTimeoutSeconds { get; set; } = 90;

    public int VisionMaxConcurrency { get; set; } = 3;

    /// <summary>Number of most-recent messages kept in the short-term context window.</summary>
    public int ContextWindowSize { get; set; } = 20;

    /// <summary>
    /// When true, messages older than the context window are folded into a running
    /// LLM summary instead of being dropped outright. Summary failures fall back to
    /// plain truncation.
    /// </summary>
    public bool EnableContextSummarization { get; set; } = false;

    /// <summary>Number of recent messages always kept verbatim when summarizing.</summary>
    public int ContextSummaryKeepRecent { get; set; } = 10;

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
    /// Effective embedding endpoint: the main chat endpoint in unified mode or when
    /// no dedicated embedding endpoint is configured.
    /// </summary>
    public string ResolveEmbeddingApiBaseUrl() =>
        UseUnifiedApi || string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl)
            ? ApiBaseUrl
            : EmbeddingApiBaseUrl;

    /// <summary>
    /// Effective embedding key: the main chat key in unified mode or when no
    /// dedicated embedding key is configured.
    /// </summary>
    public string ResolveEmbeddingApiKey() =>
        UseUnifiedApi || string.IsNullOrWhiteSpace(EmbeddingApiKey)
            ? ApiKey
            : EmbeddingApiKey;

    /// <summary>
    /// Effective vision endpoint: the main chat endpoint in unified mode, otherwise
    /// the dedicated multimodal endpoint.
    /// </summary>
    public string ResolveVisionApiBaseUrl() =>
        UseUnifiedApi ? ApiBaseUrl : VisionApiBaseUrl;

    /// <summary>
    /// Effective vision key: the main chat key in unified mode, otherwise the
    /// dedicated multimodal key.
    /// </summary>
    public string ResolveVisionApiKey() =>
        UseUnifiedApi ? ApiKey : VisionApiKey;

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
            ChatModel is "deepseek-chat" or "deepseek-reasoner" or "deepseek-v4-flash" or "default_model")
        {
            ChatModel = DefaultChatModel;
            changed = true;
        }

        // Scheme-A rollover: legacy single-model deepseek-v4-flash on an invalid/empty endpoint adopts SiliconFlow defaults
        if (ChatModel is "deepseek-v4-flash" && string.IsNullOrWhiteSpace(EmbeddingModel))
        {
            EmbeddingModel = DefaultEmbeddingModel;
            changed = true;
        }
        // Robust scheme-A vision migration: any Alibaba-derived visual model while
        // unified on SiliconFlow must adopt the SiliconFlow ID (slash-prefixed).
        var alibabaModel = VisionProviderProfiles.Get(VisionProviderPreset.AlibabaModelStudio).model;
        var silicon = VisionProviderProfiles.Get(VisionProviderPreset.SiliconFlow);
        var isAlibabaVision = VisionProviderPreset == VisionProviderPreset.AlibabaModelStudio ||
                              VisionModel == alibabaModel ||
                              VisionModel is "qwen3-vl-flash" or "qwen3-vl-plus" or "qwen-vl-max" or "qwen2-vl-2b";
        var isSiliconUnified = UseUnifiedApi &&
                               !string.IsNullOrWhiteSpace(ApiBaseUrl) &&
                               ApiBaseUrl.Contains("siliconflow.cn", StringComparison.OrdinalIgnoreCase);
        if (isAlibabaVision && (isSiliconUnified || string.IsNullOrWhiteSpace(VisionApiKey)))
        {
            // Only migrate slash-less Alibaba IDs to the slash-prefixed SiliconFlow ID
            if (!VisionModel.Contains('/'))
            {
                VisionProviderPreset = VisionProviderPreset.SiliconFlow;
                VisionProtocol = silicon.protocol;
                VisionApiBaseUrl = silicon.baseUrl;
                VisionModel = silicon.model;
                changed = true;
            }
            else if (VisionProviderPreset == VisionProviderPreset.AlibabaModelStudio)
            {
                VisionProviderPreset = VisionProviderPreset.SiliconFlow;
                VisionProtocol = silicon.protocol;
                VisionApiBaseUrl = silicon.baseUrl;
                changed = true;
            }
        }
        else if (VisionProviderPreset == VisionProviderPreset.AlibabaModelStudio &&
            string.IsNullOrWhiteSpace(VisionApiKey) &&
            VisionModel == alibabaModel)
        {
            VisionProviderPreset = VisionProviderPreset.SiliconFlow;
            VisionProtocol = silicon.protocol;
            VisionApiBaseUrl = silicon.baseUrl;
            VisionModel = silicon.model;
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
