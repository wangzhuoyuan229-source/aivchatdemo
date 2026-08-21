using System.Xml.Linq;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public class ReleaseBaselineTests
{
    private const string ExpectedVersion = "1.3.7";

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
        Assert.Contains($"## [{ExpectedVersion}] - 2026-08-22",
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

    [Fact]
    public void RecentGroupChatsExposeDeleteCommand()
    {
        var root = FindRepositoryRoot();
        var roleListView = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "RoleListView.xaml"));

        Assert.Contains("DeleteGroupChatCommand", roleListView);
        Assert.Contains("Content=\"删除\"", roleListView);
    }

    [Fact]
    public void RoleLibrarySwitchesBetweenIndependentRoleAndGroupSections()
    {
        var viewModel = new RoleListViewModel(
            null!, null!, null!, NullLogger<RoleListViewModel>.Instance, null!);

        Assert.True(viewModel.IsRolesSection);
        Assert.False(viewModel.IsGroupChatsSection);

        viewModel.ShowGroupChatsSectionCommand.Execute(null);
        Assert.False(viewModel.IsRolesSection);
        Assert.True(viewModel.IsGroupChatsSection);

        viewModel.ShowRolesSectionCommand.Execute(null);
        Assert.True(viewModel.IsRolesSection);
        Assert.False(viewModel.IsGroupChatsSection);

        var root = FindRepositoryRoot();
        var roleListView = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "RoleListView.xaml"));
        Assert.Contains("ShowRolesSectionCommand", roleListView);
        Assert.Contains("ShowGroupChatsSectionCommand", roleListView);
        Assert.Contains("IsVisible=\"{Binding IsRolesSection}\"", roleListView);
        Assert.Contains("IsVisible=\"{Binding IsGroupChatsSection}\"", roleListView);
    }

    [Fact]
    public void ChatInputLetsImeCommitBeforeConvertingInsertedLineBreakToSend()
    {
        var root = FindRepositoryRoot();
        var chatView = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "ChatView.xaml"));
        var chatViewCode = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "ChatView.xaml.cs"));

        Assert.Contains("KeyDown=\"InputBox_KeyDown\"", chatView);
        Assert.Contains("InputElement.KeyUpEvent", chatViewCode);
        Assert.Contains("RoutingStrategies.Bubble", chatViewCode);
        Assert.Contains("handledEventsToo: true", chatViewCode);
        Assert.Contains("PreeditText", chatViewCode);
        Assert.Contains("_deferCurrentEnterToIme", chatViewCode);
        Assert.Contains("TryRemoveLineBreakBeforeCaret", chatViewCode);
    }

    [Fact]
    public void SharedMemoryManagementExposesSourceRoleAndEditing()
    {
        var root = FindRepositoryRoot();
        var memoryWindow = File.ReadAllText(
            Path.Combine(root, "ChatApp.UI", "Views", "MemoryManagementWindow.xaml"));

        Assert.Contains("SourceRoleText", memoryWindow);
        Assert.Contains("EditCommand", memoryWindow);
        Assert.Contains("SaveEditCommand", memoryWindow);
        Assert.Contains("清空共享记忆", memoryWindow);
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
