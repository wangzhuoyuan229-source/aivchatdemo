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

    public static string KnowledgeImagesDir { get; } = Path.Combine(KnowledgeDir, "images");

    public static string MessageAttachmentsDir { get; } = Path.Combine(AppDataDir, "message-attachments");

    public static string LogDir { get; } = Path.Combine(AppDataDir, "logs");

    public static string ResolveKnowledgeStorageKey(string storageKey) =>
        ResolveStorageKey(KnowledgeDir, storageKey);

    public static string ResolveMessageAttachmentStorageKey(string storageKey) =>
        ResolveStorageKey(MessageAttachmentsDir, storageKey);

static AppPaths()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(KnowledgeDir);
        Directory.CreateDirectory(KnowledgeImagesDir);
        Directory.CreateDirectory(MessageAttachmentsDir);
        Directory.CreateDirectory(LogDir);
    }

    private static string ResolveStorageKey(string root, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return string.Empty;
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无效的本地存储键。");
        return resolved;
    }
}
