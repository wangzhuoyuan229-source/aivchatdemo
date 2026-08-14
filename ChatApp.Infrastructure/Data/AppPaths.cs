namespace ChatApp.Infrastructure.Data;

/// <summary>Centralized local-storage paths for the application.</summary>
public static class AppPaths
{
    /// <summary>
    /// Data directory. Defaults to %LOCALAPPDATA%\ChatApp; can be overridden via
    /// the CHATAPP_DATA_DIR environment variable (portable mode / sandbox testing).
    /// </summary>
    public static string AppDataDir { get; } =
        Environment.GetEnvironmentVariable("CHATAPP_DATA_DIR") is { Length: > 0 } env
            ? Path.GetFullPath(env)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatApp");

    public static string DbPath { get; } = Path.Combine(AppDataDir, "chatapp.db");

    public static string KnowledgeDir { get; } = Path.Combine(AppDataDir, "knowledge");

    static AppPaths()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(KnowledgeDir);
    }
}
