using ChatApp.Core.Settings;
using ChatApp.Core.Services;
using ChatApp.UI.ViewModels;

namespace ChatApp.Tests;

public class VisionProviderProfileTests
{
    [Theory]
    [InlineData(VisionProviderPreset.AlibabaModelStudio, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3-vl-flash", MultimodalApiProtocol.ChatCompletions)]
    [InlineData(VisionProviderPreset.Zhipu, "https://open.bigmodel.cn/api/paas/v4", "glm-4.6v-flash", MultimodalApiProtocol.ChatCompletions)]
    [InlineData(VisionProviderPreset.VolcengineArk, "https://ark.cn-beijing.volces.com/api/v3", "doubao-seed-2-0-lite-260215", MultimodalApiProtocol.Responses)]
    [InlineData(VisionProviderPreset.SiliconFlow, "https://api.siliconflow.cn/v1", "Qwen/Qwen3-VL-32B-Instruct", MultimodalApiProtocol.ChatCompletions)]
    public void PresetHasExpectedEditableDefaults(
        VisionProviderPreset preset,
        string baseUrl,
        string model,
        MultimodalApiProtocol protocol)
    {
        var profile = VisionProviderProfiles.Get(preset);
        Assert.Equal(baseUrl, profile.baseUrl);
        Assert.Equal(model, profile.model);
        Assert.Equal(protocol, profile.protocol);
        Assert.Equal(baseUrl, RemoteApiEndpointPolicy.NormalizeHostedApiOrThrow(baseUrl).ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task SwitchingPresetDoesNotOverwriteVisionApiKey()
    {
        var viewModel = new SettingsViewModel(new MemoryConfigurationService());
        await viewModel.LoadAsync();
        viewModel.VisionApiKey = "keep-this-key";

        viewModel.VisionProviderPresetName = "智谱开放平台";

        Assert.Equal("keep-this-key", viewModel.VisionApiKey);
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4", viewModel.VisionApiBaseUrl);
        Assert.Equal("glm-4.6v-flash", viewModel.VisionModel);
    }

    private sealed class MemoryConfigurationService : IConfigurationService
    {
        private AiSettings _settings = new();
        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);
        public Task SaveAsync(AiSettings settings, CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(false);
    }
}
