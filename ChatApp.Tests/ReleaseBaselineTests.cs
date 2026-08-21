using System.Xml.Linq;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.ViewModels;

namespace ChatApp.Tests;

public class ReleaseBaselineTests
{
    private const string ExpectedVersion = "1.3.6";

    [Fact]
    public void ProductVersionIsConsistentAcrossReleaseFiles()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "ChatApp.UI", "ChatApp.UI.csproj"));
        var version = project.Descendants("Version").Single().Value;
        var assemblyVersion = project.Descendants("AssemblyVersion").Single().Value;
        var fileVersion = project.Descendants("FileVersion").Single().Value;

        Assert.Equal(ExpectedVersion, version);
        Assert.Equal($"{ExpectedVersion}.0", assemblyVersion);
        Assert.Equal($"{ExpectedVersion}.0", fileVersion);
        Assert.Contains($"<string>{ExpectedVersion}</string>",
            File.ReadAllText(Path.Combine(root, "ChatApp.UI", "Platforms", "macOS", "Info.plist")));
        Assert.Contains($"Text=\"v{ExpectedVersion}\"",
            File.ReadAllText(Path.Combine(root, "ChatApp.UI", "MainWindow.xaml")));
        Assert.Contains($"当前版本：**v{ExpectedVersion}**",
            File.ReadAllText(Path.Combine(root, "README.md")));
        Assert.Contains($"## [{ExpectedVersion}] - 2026-08-21",
            File.ReadAllText(Path.Combine(root, "CHANGELOG.md")));
    }

    [Fact]
    public void UnfinishedGroupChatModeIsNotExposedAndMigratesToHybrid()
    {
        var settings = new AiSettings
        {
            GroupChat = new GroupChatSettings { Mode = GroupChatMode.FreeForAll }
        };

        Assert.True(settings.MigrateToRemoteApiOnly());
        Assert.Equal(GroupChatMode.Hybrid, settings.GroupChat.Mode);

        var viewModel = new SettingsViewModel(
            new MemoryConfigurationService(),
            new MemoryUiSettingsService());
        Assert.Equal(
            new[] { GroupChatMode.RoundRobin, GroupChatMode.Hybrid },
            viewModel.GroupChatModeOptions);
    }

    [Fact]
    public void UnfinishedExtensionTogglesAreNotRendered()
    {
        var root = FindRepositoryRoot();
        var settingsView = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "SettingsView.xaml"));

        Assert.DoesNotContain("启用语音交互", settingsView);
        Assert.DoesNotContain("启用角色好感度系统", settingsView);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ChatApp.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("无法定位 ChatApp 仓库根目录。");
    }

    private sealed class MemoryConfigurationService : IConfigurationService
    {
        private AiSettings settings = new();

        public Task<AiSettings> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(AiSettings value, CancellationToken ct = default)
        {
            settings = value;
            return Task.CompletedTask;
        }

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class MemoryUiSettingsService : IUiSettingsService
    {
        private UiSettings settings = new();

        public Task<UiSettings> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(UiSettings value, CancellationToken ct = default)
        {
            settings = value;
            return Task.CompletedTask;
        }
    }
}
