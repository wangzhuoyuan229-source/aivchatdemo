using System.Collections.ObjectModel;
using System.ComponentModel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatApp.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public const string NoEmbeddingPreset = "未配置";
    public const string AlibabaEmbeddingPreset = "阿里云百炼（推荐 · text-embedding-v4）";
    public const string SiliconFlowEmbeddingPreset = "SiliconFlow（BAAI/bge-m3）";
    public const string OpenAiEmbeddingPreset = "OpenAI（text-embedding-3-small）";
    public const string CustomEmbeddingPreset = "自定义远程服务";

    private readonly IConfigurationService _config;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _autoSaveCts;
    private AiSettings _settings = new();
    private bool _isApplyingEmbeddingPreset;

    private static readonly HashSet<string> PersistedPropertyNames = new()
    {
        nameof(ApiBaseUrl), nameof(ApiKey), nameof(ChatModel), nameof(EmbeddingModel),
        nameof(EmbeddingApiBaseUrl), nameof(EmbeddingApiKey), nameof(ContextWindowSize),
        nameof(MemoryTopK), nameof(KnowledgeTopK), nameof(KnowledgeMinScore),
        nameof(KnowledgeContextCharBudget), nameof(KnowledgeNeighborRadius),
        nameof(ChatTemperature), nameof(MemoryBatchSize), nameof(EnableLongTermMemory),
        nameof(EnableKnowledgeBase), nameof(EnableVoice), nameof(EnableAffinity),
        nameof(GroupChatMode), nameof(MaxSpeakersPerTurn), nameof(RespondToOtherAgents)
    };

    [ObservableProperty] private string _apiBaseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _chatModel = string.Empty;
    [ObservableProperty] private string _embeddingModel = string.Empty;
    [ObservableProperty] private string _embeddingApiBaseUrl = string.Empty;
    [ObservableProperty] private string _embeddingApiKey = string.Empty;
    [ObservableProperty] private string _embeddingProviderPreset = NoEmbeddingPreset;
    [ObservableProperty] private int _contextWindowSize = 20;
    [ObservableProperty] private int _memoryTopK = 5;
    [ObservableProperty] private int _knowledgeTopK = 5;
    [ObservableProperty] private double _knowledgeMinScore = 0.35;
    [ObservableProperty] private int _knowledgeContextCharBudget = 6000;
    [ObservableProperty] private int _knowledgeNeighborRadius = 1;
    [ObservableProperty] private double _chatTemperature = 0.65;
    [ObservableProperty] private int _memoryBatchSize = 50;
    [ObservableProperty] private bool _enableLongTermMemory;
    [ObservableProperty] private bool _enableKnowledgeBase;
    [ObservableProperty] private bool _enableVoice;
    [ObservableProperty] private bool _enableAffinity;
    [ObservableProperty] private GroupChatMode _groupChatMode = GroupChatMode.Hybrid;
    [ObservableProperty] private int _maxSpeakersPerTurn = 2;
    [ObservableProperty] private bool _respondToOtherAgents = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _providerHint = string.Empty;

    /// <summary>Group-chat mode options for the settings dropdown.</summary>
    public ObservableCollection<GroupChatMode> GroupChatModeOptions { get; } = new()
    {
        GroupChatMode.RoundRobin,
        GroupChatMode.Hybrid,
        GroupChatMode.FreeForAll
    };

    /// <summary>用户粘贴 API Key 后触发：根据 key 前缀 + 当前远程 ApiBaseUrl 综合识别服务商，
    /// 自动填充对应的 ApiBaseUrl（如为空）、ChatModel 与 EmbeddingModel，并给出提示。</summary>
    partial void OnApiKeyChanged(string value)
    {
        // 避免在加载数据时触发自动识别
        if (_isLoading) return;
        DetectProviderAndApplyModel(value, ApiBaseUrl);
    }

    /// <summary>当 ApiBaseUrl 变化时（用户从下拉选择），若已填 key 则重新识别推荐模型。</summary>
    partial void OnApiBaseUrlChanged(string value)
    {
        if (_isLoading) return;
        if (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(value))
            DetectProviderAndApplyModel(ApiKey, value);
    }

    private bool _isLoading;

    /// <summary>识别规则：根据 ApiKey 前缀 + ApiBaseUrl 域名推断服务商，自动填充默认模型。</summary>
    private void DetectProviderAndApplyModel(string apiKey, string baseUrl)
    {
        var key = (apiKey ?? string.Empty).Trim();
        var url = (baseUrl ?? string.Empty).Trim().ToLowerInvariant();

        // key 长度太短（用户还在输入中）时不触发
        if (key.Length < 8)
        {
            ProviderHint = string.Empty;
            return;
        }

        string? provider = null;
        string defaultChat = string.Empty;
        string defaultEmbed = string.Empty;
        string defaultUrl = string.Empty;

        // 规则1：基于 ApiBaseUrl 域名（最可靠）
        if (url.Contains("openai.com"))
        {
            provider = "OpenAI";
            defaultChat = "gpt-4o-mini";
            defaultEmbed = "text-embedding-3-small";
            defaultUrl = "https://api.openai.com/v1";
        }
        else if (url.Contains("deepseek.com"))
        {
            provider = "DeepSeek";
            defaultChat = "deepseek-v4-flash";
            defaultEmbed = string.Empty;
            defaultUrl = "https://api.deepseek.com/v1";
        }
        else if (url.Contains("dashscope.aliyuncs.com") || url.Contains("dashscope"))
        {
            provider = "通义千问（DashScope）";
            defaultChat = "qwen-plus";
            defaultEmbed = "text-embedding-v3";
            defaultUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1";
        }
        else if (url.Contains("siliconflow.cn") || url.Contains("siliconflow"))
        {
            provider = "SiliconFlow";
            defaultChat = "Qwen/Qwen2.5-7B-Instruct";
            defaultEmbed = "BAAI/bge-large-zh-v1.5";
            defaultUrl = "https://api.siliconflow.cn/v1";
        }
        // 规则2：基于 ApiKey 前缀的兜底识别（仅当 ApiBaseUrl 为空或未匹配时）
        else if (key.StartsWith("sk-proj-", StringComparison.OrdinalIgnoreCase))
        {
            provider = "OpenAI";
            defaultChat = "gpt-4o-mini";
            defaultEmbed = "text-embedding-3-small";
            defaultUrl = "https://api.openai.com/v1";
        }
        else
        {
            // 无法明确识别
            ProviderHint = "未能自动识别服务商，请手动选择 API Base URL";
            return;
        }

        // 应用：ApiBaseUrl 为空时自动填充
        if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            ApiBaseUrl = defaultUrl;

        // 自动选择推荐模型（用户可随后手动修改）
        ChatModel = defaultChat;
        if (provider == "DeepSeek" && string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl))
            EmbeddingModel = string.Empty;
        else if (!string.IsNullOrWhiteSpace(defaultEmbed))
            EmbeddingModel = defaultEmbed;

        ProviderHint = provider == "DeepSeek"
            ? "已识别为 DeepSeek，推荐使用 DeepSeek V4 Flash；知识库需另配远程 Embedding API"
            : $"已识别为 {provider}，已自动选择推荐模型（可手动修改）";
    }

    public ObservableCollection<string> PresetEndpoints { get; } = new()
    {
        "https://api.deepseek.com/v1",
        "https://api.openai.com/v1",
        "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "https://api.siliconflow.cn/v1"
    };

    public ObservableCollection<string> CommonChatModels { get; } = new()
    {
        "deepseek-v4-flash", "deepseek-v4-pro", "gpt-4o-mini", "gpt-4o", "qwen-plus", "qwen-turbo"
    };

    public ObservableCollection<string> CommonEmbeddingModels { get; } = new()
    {
        "text-embedding-v4", "qwen3.7-text-embedding", "BAAI/bge-m3",
        "Qwen/Qwen3-Embedding-0.6B", "Qwen/Qwen3-Embedding-4B", "Qwen/Qwen3-Embedding-8B",
        "text-embedding-3-small", "text-embedding-3-large", "text-embedding-ada-002",
        "BAAI/bge-large-zh-v1.5"
    };

    public ObservableCollection<string> EmbeddingProviderPresets { get; } = new()
    {
        NoEmbeddingPreset,
        AlibabaEmbeddingPreset,
        SiliconFlowEmbeddingPreset,
        OpenAiEmbeddingPreset,
        CustomEmbeddingPreset
    };

    partial void OnEmbeddingProviderPresetChanged(string value)
    {
        if (_isLoading || _isApplyingEmbeddingPreset) return;

        _isApplyingEmbeddingPreset = true;
        try
        {
            switch (value)
            {
                case AlibabaEmbeddingPreset:
                    EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1";
                    EmbeddingModel = "text-embedding-v4";
                    break;
                case SiliconFlowEmbeddingPreset:
                    EmbeddingApiBaseUrl = "https://api.siliconflow.cn/v1";
                    EmbeddingModel = "BAAI/bge-m3";
                    break;
                case OpenAiEmbeddingPreset:
                    EmbeddingApiBaseUrl = "https://api.openai.com/v1";
                    EmbeddingModel = "text-embedding-3-small";
                    break;
                case NoEmbeddingPreset:
                    EmbeddingApiBaseUrl = string.Empty;
                    EmbeddingModel = string.Empty;
                    EmbeddingApiKey = string.Empty;
                    EnableLongTermMemory = false;
                    EnableKnowledgeBase = false;
                    break;
            }
        }
        finally
        {
            _isApplyingEmbeddingPreset = false;
        }
    }

    partial void OnEmbeddingApiBaseUrlChanged(string value) => UpdateEmbeddingPresetFromFields();

    partial void OnEmbeddingModelChanged(string value) => UpdateEmbeddingPresetFromFields();

    private void UpdateEmbeddingPresetFromFields()
    {
        if (_isLoading || _isApplyingEmbeddingPreset) return;

        var detected = DetectEmbeddingPreset(EmbeddingApiBaseUrl, EmbeddingModel);
        if (EmbeddingProviderPreset == detected) return;

        _isApplyingEmbeddingPreset = true;
        try
        {
            EmbeddingProviderPreset = detected;
        }
        finally
        {
            _isApplyingEmbeddingPreset = false;
        }
    }

    private static string DetectEmbeddingPreset(string baseUrl, string model)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(model))
            return NoEmbeddingPreset;

        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        var normalizedModel = (model ?? string.Empty).Trim();
        if (url.Equals("https://dashscope.aliyuncs.com/compatible-mode/v1", StringComparison.OrdinalIgnoreCase) &&
            normalizedModel.Equals("text-embedding-v4", StringComparison.OrdinalIgnoreCase))
            return AlibabaEmbeddingPreset;
        if (url.Equals("https://api.siliconflow.cn/v1", StringComparison.OrdinalIgnoreCase) &&
            normalizedModel.Equals("BAAI/bge-m3", StringComparison.OrdinalIgnoreCase))
            return SiliconFlowEmbeddingPreset;
        if (url.Equals("https://api.openai.com/v1", StringComparison.OrdinalIgnoreCase) &&
            normalizedModel.Equals("text-embedding-3-small", StringComparison.OrdinalIgnoreCase))
            return OpenAiEmbeddingPreset;
        return CustomEmbeddingPreset;
    }

    public SettingsViewModel(IConfigurationService config)
    {
        _config = config;
        PropertyChanged += OnSettingPropertyChanged;
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoading || e.PropertyName is null || !PersistedPropertyNames.Contains(e.PropertyName))
            return;

        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        StatusText = "等待自动保存…";
        _ = AutoSaveAfterDelayAsync(_autoSaveCts);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(700, source.Token);
            if (!ReferenceEquals(source, _autoSaveCts)) return;
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer edit restarted the debounce timer.
        }
        catch (Exception ex)
        {
            StatusText = $"自动保存失败：{ex.Message}";
        }
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            _settings = await _config.LoadAsync();
            ApiBaseUrl = _settings.ApiBaseUrl;
            ApiKey = _settings.ApiKey;
            ChatModel = _settings.ChatModel;
            EmbeddingModel = _settings.EmbeddingModel;
            EmbeddingApiBaseUrl = _settings.EmbeddingApiBaseUrl;
            EmbeddingApiKey = _settings.EmbeddingApiKey;
            EmbeddingProviderPreset = DetectEmbeddingPreset(
                _settings.EmbeddingApiBaseUrl, _settings.EmbeddingModel);
            ContextWindowSize = _settings.ContextWindowSize;
            MemoryTopK = _settings.MemoryTopK;
            KnowledgeTopK = _settings.KnowledgeTopK;
            KnowledgeMinScore = _settings.KnowledgeMinScore;
            KnowledgeContextCharBudget = _settings.KnowledgeContextCharBudget;
            KnowledgeNeighborRadius = _settings.KnowledgeNeighborRadius;
            ChatTemperature = _settings.ChatTemperature;
            MemoryBatchSize = _settings.MemoryBatchSize;
            EnableLongTermMemory = _settings.EnableLongTermMemory;
            EnableKnowledgeBase = _settings.EnableKnowledgeBase;
            EnableVoice = _settings.EnableVoice;
            EnableAffinity = _settings.EnableAffinity;
            GroupChatMode = _settings.GroupChat.Mode;
            MaxSpeakersPerTurn = _settings.GroupChat.MaxSpeakersPerTurn;
            RespondToOtherAgents = _settings.GroupChat.RespondToOtherAgents;
            StatusText = string.Empty;
            ProviderHint = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            StatusText = "正在自动保存…";
            await SaveCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"自动保存失败：{ex.Message}";
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task SaveCoreAsync()
    {
        if (!RemoteApiEndpointPolicy.TryNormalize(ApiBaseUrl, out var chatEndpoint, out var chatError))
        {
            StatusText = $"自动保存失败：{chatError}";
            return;
        }

        Uri? embeddingEndpoint = null;
        if (!string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl) &&
            !RemoteApiEndpointPolicy.TryNormalize(EmbeddingApiBaseUrl, out embeddingEndpoint, out var embeddingError))
        {
            StatusText = $"自动保存失败：Embedding 端点{embeddingError}";
            return;
        }

        _settings.ApiBaseUrl = chatEndpoint.ToString().TrimEnd('/');
        _settings.ApiKey = ApiKey;
        _settings.ChatModel = ChatModel;
        _settings.EmbeddingModel = EmbeddingModel;
        _settings.EmbeddingApiBaseUrl = embeddingEndpoint?.ToString().TrimEnd('/') ?? string.Empty;
        _settings.EmbeddingApiKey = EmbeddingApiKey;
        _settings.ContextWindowSize = Math.Clamp(ContextWindowSize, 4, 200);
        _settings.MemoryTopK = Math.Clamp(MemoryTopK, 1, 50);
        _settings.KnowledgeTopK = Math.Clamp(KnowledgeTopK, 1, 50);
        _settings.KnowledgeMinScore = Math.Clamp(KnowledgeMinScore, 0, 1);
        _settings.KnowledgeContextCharBudget = Math.Clamp(KnowledgeContextCharBudget, 200, 50_000);
        _settings.KnowledgeNeighborRadius = Math.Clamp(KnowledgeNeighborRadius, 0, 3);
        _settings.ChatTemperature = Math.Clamp(ChatTemperature, 0, 2);
        _settings.MemoryBatchSize = Math.Clamp(MemoryBatchSize, 2, 1000);
        _settings.EnableLongTermMemory = EnableLongTermMemory;
        _settings.EnableKnowledgeBase = EnableKnowledgeBase;
        _settings.EnableVoice = EnableVoice;
        _settings.EnableAffinity = EnableAffinity;
        _settings.GroupChat.Mode = GroupChatMode;
        _settings.GroupChat.MaxSpeakersPerTurn = Math.Clamp(MaxSpeakersPerTurn, 1, 20);
        _settings.GroupChat.RespondToOtherAgents = RespondToOtherAgents;

        await _config.SaveAsync(_settings);
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ChatModel))
        {
            StatusText = "已自动保存；填写 API Key 和聊天模型后即可对话";
        }
        else if ((EnableLongTermMemory || EnableKnowledgeBase) &&
                 (string.IsNullOrWhiteSpace(EmbeddingModel) ||
                  string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl) &&
                  chatEndpoint.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "聊天配置已自动保存 ✓；RAG 仍需独立远程 Embedding API";
        }
        else
        {
            StatusText = "已自动保存 ✓";
        }
    }
}
