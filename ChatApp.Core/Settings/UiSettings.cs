namespace ChatApp.Core.Settings;

/// <summary>Application theme preference.</summary>
public enum ThemeMode
{
    Light = 0,
    Dark = 1,
    FollowSystem = 2
}

/// <summary>UI-only preferences (theme, reading), persisted separately from AI settings.</summary>
public class UiSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Light;

    /// <summary>Chat bubble font size in points (12–22).</summary>
    public double ChatFontSize { get; set; } = 14;
}
