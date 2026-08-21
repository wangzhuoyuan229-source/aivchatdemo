namespace ChatApp.Core.Settings;

public sealed record DeveloperSocial(string Label, string Url, string Icon, string? CopyText = null);

public static class DeveloperSocials
{
    // Displayed via 帮助与支持弹窗. Keep URLs public, no secrets.
    // Icon uses emoji for zero-asset portability; replace with Assets if needed.
    public static IReadOnlyList<DeveloperSocial> All { get; } = new List<DeveloperSocial>
    {
        new("GitHub", "https://github.com/wangzhuoyuan229-source/aivchatdemo", "🐙"),
        new("B站", "https://space.bilibili.com/451598529", "📺", "UID:451598529"),
        new("小红书", "https://www.xiaohongshu.com/user/profile/7439240082", "📕", "7439240082"),
        new("邮箱", "mailto:1037561013@qq.com", "✉️", "1037561013@qq.com"),
        new("反馈与帮助", "https://github.com/anomalyco/opencode/issues", "💬"),
    };
}
