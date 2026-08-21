namespace ChatApp.Core.Settings;

public sealed record DeveloperSocial(
    string Label,
    string Url,
    string Icon,
    string? CopyText = null,
    string? IconAsset = null)
{
    public bool HasIconAsset => !string.IsNullOrWhiteSpace(IconAsset);
}

public static class DeveloperSocials
{
    // Displayed via 帮助与支持弹窗. Keep URLs public, no secrets.
    public static IReadOnlyList<DeveloperSocial> All { get; } = new List<DeveloperSocial>
    {
        new("GitHub", "https://github.com/wangzhuoyuan229-source/aivchatdemo", "🐙", IconAsset: "avares://ChatApp.UI/Assets/Social/github.png"),
        new("B站", "https://space.bilibili.com/451598529", "📺", "UID:451598529", "avares://ChatApp.UI/Assets/Social/bilibili.png"),
        new("小红书", "https://www.xiaohongshu.com/user/profile/7439240082", "📕", "7439240082", "avares://ChatApp.UI/Assets/Social/xiaohongshu.png"),
        new("邮箱", "mailto:1037561013@qq.com", "✉️", "1037561013@qq.com", "avares://ChatApp.UI/Assets/Social/email.png"),
        new("反馈与帮助", "https://github.com/anomalyco/opencode/issues", "💬"),
    };
}
