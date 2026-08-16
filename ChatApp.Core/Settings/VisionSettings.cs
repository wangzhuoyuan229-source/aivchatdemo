namespace ChatApp.Core.Settings;

public enum VisionProviderPreset
{
    AlibabaModelStudio = 0,
    Zhipu = 1,
    VolcengineArk = 2,
    SiliconFlow = 3,
    Custom = 4
}

public enum MultimodalApiProtocol
{
    ChatCompletions = 0,
    Responses = 1
}

public static class VisionProviderProfiles
{
    public static (string baseUrl, string model, MultimodalApiProtocol protocol) Get(VisionProviderPreset preset) => preset switch
    {
        VisionProviderPreset.AlibabaModelStudio =>
            ("https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3-vl-flash", MultimodalApiProtocol.ChatCompletions),
        VisionProviderPreset.Zhipu =>
            ("https://open.bigmodel.cn/api/paas/v4", "glm-4.6v-flash", MultimodalApiProtocol.ChatCompletions),
        VisionProviderPreset.VolcengineArk =>
            ("https://ark.cn-beijing.volces.com/api/v3", "doubao-seed-2-0-lite-260215", MultimodalApiProtocol.Responses),
        VisionProviderPreset.SiliconFlow =>
            ("https://api.siliconflow.cn/v1", "Qwen/Qwen3-VL-32B-Instruct", MultimodalApiProtocol.ChatCompletions),
        _ => (string.Empty, string.Empty, MultimodalApiProtocol.ChatCompletions)
    };
}
