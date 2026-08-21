namespace ChatApp.Core.Settings;

public enum UnifiedApiPreset
{
    SiliconFlow = 0,
    AlibabaDashScope = 1,
    OpenAI = 2,
    Custom = 3
}

public static class UnifiedApiPresetProfiles
{
    public static (string baseUrl, string chatModel, string embeddingModel, string visionModel, MultimodalApiProtocol visionProtocol, VisionProviderPreset visionPreset) Get(UnifiedApiPreset preset) => preset switch
    {
        UnifiedApiPreset.SiliconFlow =>
            ("https://api.siliconflow.cn/v1", AiSettings.DefaultChatModel, "BAAI/bge-m3", "Qwen/Qwen3-VL-32B-Instruct", MultimodalApiProtocol.ChatCompletions, VisionProviderPreset.SiliconFlow),
        UnifiedApiPreset.AlibabaDashScope =>
            ("https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "text-embedding-v4", "qwen3-vl-flash", MultimodalApiProtocol.ChatCompletions, VisionProviderPreset.AlibabaModelStudio),
        UnifiedApiPreset.OpenAI =>
            ("https://api.openai.com/v1", "gpt-4o-mini", "text-embedding-3-small", "gpt-4o", MultimodalApiProtocol.ChatCompletions, VisionProviderPreset.Custom),
        _ => (string.Empty, string.Empty, string.Empty, string.Empty, MultimodalApiProtocol.ChatCompletions, VisionProviderPreset.Custom)
    };

    public static UnifiedApiPreset Detect(string? baseUrl, string? chatModel, string? embeddingModel)
    {
        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
        var chat = (chatModel ?? string.Empty).Trim();
        var embed = (embeddingModel ?? string.Empty).Trim();
        if (url == "https://api.siliconflow.cn/v1" && chat == AiSettings.DefaultChatModel && embed == "BAAI/bge-m3")
            return UnifiedApiPreset.SiliconFlow;
        if (url == "https://dashscope.aliyuncs.com/compatible-mode/v1" && chat == "qwen-plus" && embed == "text-embedding-v4")
            return UnifiedApiPreset.AlibabaDashScope;
        if (url == "https://api.openai.com/v1" && chat == "gpt-4o-mini" && embed == "text-embedding-3-small")
            return UnifiedApiPreset.OpenAI;
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(chat) && string.IsNullOrWhiteSpace(embed))
            return UnifiedApiPreset.SiliconFlow;
        return UnifiedApiPreset.Custom;
    }
}
