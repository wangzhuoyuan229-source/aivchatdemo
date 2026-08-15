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
        var viewModel = new SettingsViewModel(config);
        await viewModel.LoadAsync();

        viewModel.EnableKnowledgeBase = true;
        viewModel.ApiKey = "sk-test-autosave";

        var saved = await config.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        Assert.Equal("sk-test-autosave", saved.ApiKey);
        Assert.Equal("deepseek-v4-flash", saved.ChatModel);
        Assert.True(saved.EnableKnowledgeBase);
        Assert.Contains("RAG", viewModel.StatusText);
    }

    [Fact]
    public async Task AlibabaPresetFillsAndPersistsEmbeddingSettings()
    {
        var config = new RecordingConfigurationService();
        var viewModel = new SettingsViewModel(config);
        await viewModel.LoadAsync();

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
}
