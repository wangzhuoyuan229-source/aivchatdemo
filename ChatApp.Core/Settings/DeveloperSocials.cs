namespace ChatApp.Core.Settings;

public sealed record DeveloperSocial(string Label, string Url, string Icon, string? CopyText = null);

public static class DeveloperSocials
{
    // Displayed via Settings → 联系开发者. Keep URLs public, no secrets.
    // Icon uses emoji for zero-asset portability; replace with Assets if needed.
    public static IReadOnlyList<DeveloperSocial> All { get; } = new List<DeveloperSocial>
    {
        new("GitHub", "https://github.com/wangzhuoyuan229-source/aivchatdemo", "🐙"),
        new("邮箱", "mailto:support@chatapp.local", "✉️", "support@chatapp.local"),
        new("反馈与帮助", "https://github.com/anomalyco/opencode/issues", "💬"),
    };
}
