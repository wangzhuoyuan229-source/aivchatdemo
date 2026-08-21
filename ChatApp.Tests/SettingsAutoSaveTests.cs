using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.ViewModels;

namespace ChatApp.Tests;

public class SettingsAutoSaveTests
{
    [Fact]
    public async Task ChangingApiKeyAutomaticallyPersistsAfterDebounce()
    {
        var config = new RecordingConfigurationService();
        var viewModel = new SettingsViewModel(config, new MemoryUiSettingsService());
        await viewModel.LoadAsync();

        viewModel.EnableKnowledgeBase = true;
        viewModel.ApiKey = "sk-test-autosave";

        var saved = await config.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        Assert.Equal("sk-test-autosave", saved.ApiKey);
        Assert.Equal("deepseek-ai/DeepSeek-V4-Flash", saved.ChatModel);
        Assert.True(saved.EnableKnowledgeBase);
        Assert.Contains("已自动保存", viewModel.StatusText);
    }

    [Fact]
    public async Task AlibabaPresetFillsAndPersistsEmbeddingSettings()
    {
        var config = new RecordingConfigurationService();
        var viewModel = new SettingsViewModel(config, new MemoryUiSettingsService());
        await viewModel.LoadAsync();

        viewModel.UseUnifiedApi = false;
        viewModel.EmbeddingApiKey = "sk-test-bailian";
        viewModel.EmbeddingProviderPreset = SettingsViewModel.AlibabaEmbeddingPreset;

        var saved = await config.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("text-embedding-v4", saved.EmbeddingModel);
        Assert.Equal(
            "https://dashscope.aliyuncs.com/compatible-mode/v1",
            saved.EmbeddingApiBaseUrl);
        Assert.Equal("sk-test-bailian", saved.EmbeddingApiKey);
    }

    private sealed class RecordingConfigurationService : IConfigurationService
    {
        public TaskCompletionSource<AiSettings> Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiSettings> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new AiSettings());

        public Task SaveAsync(AiSettings settings, CancellationToken ct = default)
        {
            Saved.TrySetResult(settings);
            return Task.CompletedTask;
        }

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class MemoryUiSettingsService : IUiSettingsService
    {
        private UiSettings _settings = new();
        public Task<UiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);
        public Task SaveAsync(UiSettings settings, CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }
}
