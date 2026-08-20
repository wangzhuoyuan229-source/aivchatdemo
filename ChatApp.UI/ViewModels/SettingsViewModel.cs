using System.Collections.ObjectModel;
using System.ComponentModel;
using ChatApp.Core.Security;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatApp.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public const string NoEmbeddingPreset = "未配置";
    public const string AlibabaEmbeddingPreset = "阿里云百炼（推荐 · text-embedding-v4）";
    public const string SiliconFlowEmbeddingPreset = "SiliconFlow（BAAI/bge-m3）";
    public const string OpenAiEmbeddingPreset = "OpenAI（text-embedding-3-small）";
    public const string CustomEmbeddingPreset = "自定义远程服务";

    private readonly IConfigurationService _config;
    private readonly IUiSettingsService _uiConfig;
    private readonly IImageDescriptionService? _imageDescriptions;
    private readonly IApiProbeService? _apiProbe;
    private readonly KnowledgeViewModel? _knowledgeViewModel;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _autoSaveCts;
    private AiSettings _settings = new();
    private bool _isApplyingEmbeddingPreset;
    private bool _isApplyingVisionPreset;
    private bool _isLoadingUiSettings;

    private static readonly HashSet<string> PersistedPropertyNames = new()
    {
        nameof(UseUnifiedApi),
        nameof(ApiBaseUrl), nameof(ApiKey), nameof(ChatModel), nameof(EmbeddingModel),
        nameof(EmbeddingApiBaseUrl), nameof(EmbeddingApiKey), nameof(ContextWindowSize),
        nameof(MemoryTopK), nameof(KnowledgeTopK), nameof(KnowledgeMinScore),
        nameof(VisionProviderPresetName), nameof(VisionProtocol), nameof(VisionApiBaseUrl),
        nameof(VisionApiKey), nameof(VisionModel), nameof(VisionTimeoutSeconds),
        nameof(VisionMaxConcurrency), nameof(KnowledgeImageTopK), nameof(KnowledgeImageMinScore),
        nameof(KnowledgeContextCharBudget), nameof(KnowledgeNeighborRadius),
        nameof(ChatTemperature), nameof(MemoryBatchSize), nameof(EnableLongTermMemory),
        nameof(EnableKnowledgeBase), nameof(EnableVoice), nameof(EnableAffinity),
        nameof(EnableContextSummarization), nameof(ContextSummaryKeepRecent),
        nameof(GroupChatMode), nameof(MaxSpeakersPerTurn), nameof(RespondToOtherAgents)
    };

    [ObservableProperty] private bool _useUnifiedApi;
    [ObservableProperty] private string _apiBaseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _chatModel = string.Empty;
    [ObservableProperty] private string _embeddingModel = string.Empty;
    [ObservableProperty] private string _embeddingApiBaseUrl = string.Empty;
    [ObservableProperty] private string _embeddingApiKey = string.Empty;
    [ObservableProperty] private string _embeddingProviderPreset = NoEmbeddingPreset;
    [ObservableProperty] private string _visionProviderPresetName = VisionPresetNames.Alibaba;
    [ObservableProperty] private MultimodalApiProtocol _visionProtocol = MultimodalApiProtocol.ChatCompletions;
    [ObservableProperty] private string _visionApiBaseUrl = string.Empty;
    [ObservableProperty] private string _visionApiKey = string.Empty;
    [ObservableProperty] private string _visionModel = string.Empty;
    [ObservableProperty] private int _visionTimeoutSeconds = 90;
    [ObservableProperty] private int _visionMaxConcurrency = 3;
    [ObservableProperty] private int _contextWindowSize = 20;
    [ObservableProperty] private int _memoryTopK = 5;
    [ObservableProperty] private int _knowledgeTopK = 5;
    [ObservableProperty] private double _knowledgeMinScore = 0.35;
    [ObservableProperty] private int _knowledgeImageTopK = 5;
    [ObservableProperty] private double _knowledgeImageMinScore = 0.35;
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
    [ObservableProperty] private bool _isTestingVision;
    [ObservableProperty] private string _visionTestStatus = string.Empty;
    [ObservableProperty] private bool _isTestingChat;
    [ObservableProperty] private string _chatTestStatus = string.Empty;
    [ObservableProperty] private bool _isTestingEmbedding;
    [ObservableProperty] private string _embeddingTestStatus = string.Empty;

    // ----- 2.5 长对话摘要压缩 / 2.6 外观 -----
    [ObservableProperty] private bool _enableContextSummarization;
    [ObservableProperty] private int _contextSummaryKeepRecent = 10;
    [ObservableProperty] private ThemeMode _themeMode = ThemeMode.Light;
    [ObservableProperty] private double _chatFontSize = 14;

    public ObservableCollection<ThemeMode> ThemeModeOptions { get; } = new()
    {
        ThemeMode.Light,
        ThemeMode.Dark,
        ThemeMode.FollowSystem
    };

    public ThemeMode[] ThemeModes => new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.FollowSystem };

    /// <summary>Applies theme immediately when the dropdown changes.</summary>
    partial void OnThemeModeChanged(ThemeMode value)
    {
        if (_isLoadingUiSettings) return;
        ThemeService.Apply(value);
        _ = SaveUiSettingsAsync();
    }

    partial void OnChatFontSizeChanged(double value)
    {
        if (_isLoadingUiSettings) return;
        _ = SaveUiSettingsAsync();
    }

    private async Task SaveUiSettingsAsync()
    {
        try
        {
            await _uiConfig.SaveAsync(new UiSettings
            {
                Theme = ThemeMode,
                ChatFontSize = ChatFontSize
            });
        }
        catch (Exception ex)
        {
            StatusText = $"外观设置保存失败：{ex.Message}";
        }
    }

    private static class VisionPresetNames
    {
        public const string Alibaba = "阿里云百炼（推荐）";
        public const string Zhipu = "智谱开放平台";
        public const string Volcengine = "火山方舟";
        public const string SiliconFlow = "SiliconFlow";
        public const string Custom = "自定义 OpenAI 兼容服务";
    }

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

    public ObservableCollection<string> VisionProviderPresetOptions { get; } = new()
    {
        VisionPresetNames.Alibaba,
        VisionPresetNames.Zhipu,
        VisionPresetNames.Volcengine,
        VisionPresetNames.SiliconFlow,
        VisionPresetNames.Custom
    };

    public ObservableCollection<MultimodalApiProtocol> VisionProtocolOptions { get; } = new()
    {
        MultimodalApiProtocol.ChatCompletions,
        MultimodalApiProtocol.Responses
    };

    partial void OnVisionProviderPresetNameChanged(string value)
    {
        if (_isLoading || _isApplyingVisionPreset) return;
        var preset = ParseVisionPreset(value);
        if (preset == VisionProviderPreset.Custom) return;
        var profile = VisionProviderProfiles.Get(preset);
        _isApplyingVisionPreset = true;
        try
        {
            VisionProtocol = profile.protocol;
            VisionApiBaseUrl = profile.baseUrl;
            VisionModel = profile.model;
            // The API key deliberately remains untouched when switching presets.
        }
        finally
        {
            _isApplyingVisionPreset = false;
        }
    }

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

    public SettingsViewModel(
        IConfigurationService config,
        IUiSettingsService uiConfig,
        IImageDescriptionService? imageDescriptions = null,
        KnowledgeViewModel? knowledgeViewModel = null,
        IApiProbeService? apiProbe = null)
    {
        _config = config;
        _uiConfig = uiConfig;
        _imageDescriptions = imageDescriptions;
        _knowledgeViewModel = knowledgeViewModel;
        _apiProbe = apiProbe;
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
            RegisterSecrets();
            UseUnifiedApi = _settings.UseUnifiedApi;
            ApiBaseUrl = _settings.ApiBaseUrl;
            ApiKey = _settings.ApiKey;
            ChatModel = _settings.ChatModel;
            EmbeddingModel = _settings.EmbeddingModel;
            EmbeddingApiBaseUrl = _settings.EmbeddingApiBaseUrl;
            EmbeddingApiKey = _settings.EmbeddingApiKey;
            EmbeddingProviderPreset = DetectEmbeddingPreset(
                _settings.EmbeddingApiBaseUrl, _settings.EmbeddingModel);
            VisionProviderPresetName = FormatVisionPreset(_settings.VisionProviderPreset);
            VisionProtocol = _settings.VisionProtocol;
            VisionApiBaseUrl = _settings.VisionApiBaseUrl;
            VisionApiKey = _settings.VisionApiKey;
            VisionModel = _settings.VisionModel;
            VisionTimeoutSeconds = _settings.VisionTimeoutSeconds;
            VisionMaxConcurrency = _settings.VisionMaxConcurrency;
            ContextWindowSize = _settings.ContextWindowSize;
            EnableContextSummarization = _settings.EnableContextSummarization;
            ContextSummaryKeepRecent = _settings.ContextSummaryKeepRecent;
            MemoryTopK = _settings.MemoryTopK;
            KnowledgeTopK = _settings.KnowledgeTopK;
            KnowledgeMinScore = _settings.KnowledgeMinScore;
            KnowledgeImageTopK = _settings.KnowledgeImageTopK;
            KnowledgeImageMinScore = _settings.KnowledgeImageMinScore;
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
            VisionTestStatus = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }

        _isLoadingUiSettings = true;
        try
        {
            var ui = await _uiConfig.LoadAsync();
            ThemeMode = ui.Theme;
            ChatFontSize = ui.ChatFontSize;
        }
        finally
        {
            _isLoadingUiSettings = false;
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

    private async Task<bool> SaveCoreAsync()
    {
        if (!RemoteApiEndpointPolicy.TryNormalize(ApiBaseUrl, out var chatEndpoint, out var chatError))
        {
            StatusText = $"自动保存失败：{chatError}";
            return false;
        }

        Uri? embeddingEndpoint = null;
        if (!UseUnifiedApi &&
            !string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl) &&
            !RemoteApiEndpointPolicy.TryNormalize(EmbeddingApiBaseUrl, out embeddingEndpoint, out var embeddingError))
        {
            StatusText = $"自动保存失败：Embedding 端点{embeddingError}";
            return false;
        }

        Uri? visionEndpoint = null;
        if (!UseUnifiedApi &&
            !string.IsNullOrWhiteSpace(VisionApiBaseUrl) &&
            !RemoteApiEndpointPolicy.TryNormalizeHostedApi(VisionApiBaseUrl, out visionEndpoint, out var visionError))
        {
            StatusText = $"自动保存失败：多模态端点{visionError}";
            return false;
        }

        _settings.UseUnifiedApi = UseUnifiedApi;
        _settings.ApiBaseUrl = chatEndpoint.ToString().TrimEnd('/');
        _settings.ApiKey = ApiKey;
        _settings.ChatModel = ChatModel;
        _settings.EmbeddingModel = EmbeddingModel;
        if (!UseUnifiedApi)
        {
            // In unified mode the independent endpoint/key fields are hidden and
            // ignored at runtime; keep the stored values so switching back to
            // independent mode restores the previous configuration.
            _settings.EmbeddingApiBaseUrl = embeddingEndpoint?.ToString().TrimEnd('/') ?? string.Empty;
            _settings.EmbeddingApiKey = EmbeddingApiKey;
        }
        _settings.VisionProviderPreset = ParseVisionPreset(VisionProviderPresetName);
        _settings.VisionProtocol = VisionProtocol;
        if (!UseUnifiedApi)
        {
            _settings.VisionApiBaseUrl = visionEndpoint?.ToString().TrimEnd('/') ?? string.Empty;
            _settings.VisionApiKey = VisionApiKey;
        }
        _settings.VisionModel = VisionModel.Trim();
        _settings.VisionTimeoutSeconds = Math.Clamp(VisionTimeoutSeconds, 10, 600);
        _settings.VisionMaxConcurrency = Math.Clamp(VisionMaxConcurrency, 1, 3);
        _settings.ContextWindowSize = Math.Clamp(ContextWindowSize, 4, 200);
        _settings.EnableContextSummarization = EnableContextSummarization;
        _settings.ContextSummaryKeepRecent = Math.Clamp(ContextSummaryKeepRecent, 2, 200);
        _settings.MemoryTopK = Math.Clamp(MemoryTopK, 1, 50);
        _settings.KnowledgeTopK = Math.Clamp(KnowledgeTopK, 1, 50);
        _settings.KnowledgeMinScore = Math.Clamp(KnowledgeMinScore, 0, 1);
        _settings.KnowledgeImageTopK = Math.Clamp(KnowledgeImageTopK, 1, 20);
        _settings.KnowledgeImageMinScore = Math.Clamp(KnowledgeImageMinScore, 0, 1);
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
        RegisterSecrets();
        if (_settings.EnableKnowledgeBase &&
            !string.IsNullOrWhiteSpace(_settings.EmbeddingModel) &&
            !string.IsNullOrWhiteSpace(_settings.ResolveEmbeddingApiKey()))
        {
            _ = _knowledgeViewModel?.ImportBundledKnowledgeAsync();
        }
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ChatModel))
        {
            StatusText = "已自动保存；填写 API Key 和聊天模型后即可对话";
        }
        else if ((EnableLongTermMemory || EnableKnowledgeBase) &&
                 (string.IsNullOrWhiteSpace(EmbeddingModel) ||
                  (UseUnifiedApi || string.IsNullOrWhiteSpace(EmbeddingApiBaseUrl)) &&
                  chatEndpoint.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = UseUnifiedApi
                ? "聊天配置已自动保存 ✓；当前统一端点不提供 Embedding，建议改用聚合平台或切换为三路独立模式"
                : "聊天配置已自动保存 ✓；RAG 仍需独立远程 Embedding API";
        }
        else
        {
            StatusText = "已自动保存 ✓";
        }
        return true;
    }

    /// <summary>Registers the active API keys with the log redactor (never persisted there).</summary>
    private void RegisterSecrets()
    {
        SecretRedaction.Register(_settings.ApiKey);
        SecretRedaction.Register(_settings.ResolveEmbeddingApiKey());
        SecretRedaction.Register(_settings.ResolveVisionApiKey());
    }

    [RelayCommand]
    private async Task TestChatConnectionAsync()
    {
        if (_apiProbe is null)
        {
            ChatTestStatus = "连接测试服务未注册。";
            return;
        }
        IsTestingChat = true;
        ChatTestStatus = "正在发送最小聊天请求…";
        try
        {
            await _saveGate.WaitAsync();
            try
            {
                if (!await SaveCoreAsync())
                {
                    ChatTestStatus = StatusText;
                    return;
                }
            }
            finally { _saveGate.Release(); }
            var result = await _apiProbe.TestChatAsync();
            ChatTestStatus = result.IsSuccess ? result.Message : $"连接失败：{result.Message}";
        }
        catch (Exception ex)
        {
            ChatTestStatus = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTestingChat = false;
        }
    }

    [RelayCommand]
    private async Task TestEmbeddingConnectionAsync()
    {
        if (_apiProbe is null)
        {
            EmbeddingTestStatus = "连接测试服务未注册。";
            return;
        }
        IsTestingEmbedding = true;
        EmbeddingTestStatus = "正在发送最小 Embedding 请求…";
        try
        {
            await _saveGate.WaitAsync();
            try
            {
                if (!await SaveCoreAsync())
                {
                    EmbeddingTestStatus = StatusText;
                    return;
                }
            }
            finally { _saveGate.Release(); }
            var result = await _apiProbe.TestEmbeddingAsync();
            EmbeddingTestStatus = result.IsSuccess ? result.Message : $"连接失败：{result.Message}";
        }
        catch (Exception ex)
        {
            EmbeddingTestStatus = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTestingEmbedding = false;
        }
    }

    [RelayCommand]
    private async Task TestVisionConnectionAsync()
    {
        if (_imageDescriptions is null)
        {
            VisionTestStatus = "图片识别服务未注册。";
            return;
        }

        IsTestingVision = true;
        VisionTestStatus = "正在验证鉴权、图片输入和响应解析…";
        try
        {
            await _saveGate.WaitAsync();
            try
            {
                if (!await SaveCoreAsync())
                {
                    VisionTestStatus = StatusText;
                    return;
                }
            }
            finally { _saveGate.Release(); }
            var result = await _imageDescriptions.TestConnectionAsync();
            VisionTestStatus = result.IsSuccess
                ? $"连接成功：{result.Provider} / {result.Model}"
                : $"连接失败：{result.ErrorDetail}";
        }
        catch (Exception ex)
        {
            VisionTestStatus = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTestingVision = false;
        }
    }

    private static VisionProviderPreset ParseVisionPreset(string value) => value switch
    {
        VisionPresetNames.Zhipu => VisionProviderPreset.Zhipu,
        VisionPresetNames.Volcengine => VisionProviderPreset.VolcengineArk,
        VisionPresetNames.SiliconFlow => VisionProviderPreset.SiliconFlow,
        VisionPresetNames.Custom => VisionProviderPreset.Custom,
        _ => VisionProviderPreset.AlibabaModelStudio
    };

    private static string FormatVisionPreset(VisionProviderPreset value) => value switch
    {
        VisionProviderPreset.Zhipu => VisionPresetNames.Zhipu,
        VisionProviderPreset.VolcengineArk => VisionPresetNames.Volcengine,
        VisionProviderPreset.SiliconFlow => VisionPresetNames.SiliconFlow,
        VisionProviderPreset.Custom => VisionPresetNames.Custom,
        _ => VisionPresetNames.Alibaba
    };
}
