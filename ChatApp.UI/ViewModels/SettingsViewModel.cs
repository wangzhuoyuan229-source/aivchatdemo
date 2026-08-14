using System.Collections.ObjectModel;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatApp.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _config;
    private AiSettings _settings = new();

    [ObservableProperty] private string _apiBaseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _chatModel = string.Empty;
    [ObservableProperty] private string _embeddingModel = string.Empty;
    [ObservableProperty] private int _contextWindowSize = 20;
    [ObservableProperty] private int _memoryTopK = 5;
    [ObservableProperty] private int _knowledgeTopK = 5;
    [ObservableProperty] private int _memoryBatchSize = 50;
    [ObservableProperty] private bool _enableLongTermMemory = true;
    [ObservableProperty] private bool _enableKnowledgeBase = true;
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

    /// <summary>用户粘贴 API Key 后触发：根据 key 前缀 + 当前 ApiBaseUrl 综合识别服务商，
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
            defaultChat = "deepseek-chat";
            defaultEmbed = "BAAI/bge-large-zh-v1.5"; // DeepSeek 自家暂无 embedding，提示用兼容端
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
        else if (url.Contains("localhost:11434") || url.Contains("127.0.0.1:11434"))
        {
            provider = "Ollama（本地）";
            defaultChat = "llama3";
            defaultEmbed = "nomic-embed-text";
            defaultUrl = "http://localhost:11434/v1";
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
        EmbeddingModel = defaultEmbed;

        ProviderHint = $"已识别为 {provider}，已自动选择推荐模型（可手动修改）";
    }

    public ObservableCollection<string> PresetEndpoints { get; } = new()
    {
        "https://api.openai.com/v1",
        "https://api.deepseek.com/v1",
        "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "https://api.siliconflow.cn/v1",
        "http://localhost:11434/v1"
    };

    public ObservableCollection<string> CommonChatModels { get; } = new()
    {
        "gpt-4o-mini", "gpt-4o", "deepseek-chat", "deepseek-reasoner", "qwen-plus", "qwen-turbo"
    };

    public ObservableCollection<string> CommonEmbeddingModels { get; } = new()
    {
        "text-embedding-3-small", "text-embedding-3-large", "text-embedding-ada-002", "BAAI/bge-large-zh-v1.5"
    };

    public SettingsViewModel(IConfigurationService config) => _config = config;

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
            ContextWindowSize = _settings.ContextWindowSize;
            MemoryTopK = _settings.MemoryTopK;
            KnowledgeTopK = _settings.KnowledgeTopK;
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

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.ApiBaseUrl = ApiBaseUrl;
        _settings.ApiKey = ApiKey;
        _settings.ChatModel = ChatModel;
        _settings.EmbeddingModel = EmbeddingModel;
        _settings.ContextWindowSize = ContextWindowSize;
        _settings.MemoryTopK = MemoryTopK;
        _settings.KnowledgeTopK = KnowledgeTopK;
        _settings.MemoryBatchSize = MemoryBatchSize;
        _settings.EnableLongTermMemory = EnableLongTermMemory;
        _settings.EnableKnowledgeBase = EnableKnowledgeBase;
        _settings.EnableVoice = EnableVoice;
        _settings.EnableAffinity = EnableAffinity;
        _settings.GroupChat.Mode = GroupChatMode;
        _settings.GroupChat.MaxSpeakersPerTurn = MaxSpeakersPerTurn;
        _settings.GroupChat.RespondToOtherAgents = RespondToOtherAgents;

        await _config.SaveAsync(_settings);
        StatusText = "已保存 ✓";
    }
}
