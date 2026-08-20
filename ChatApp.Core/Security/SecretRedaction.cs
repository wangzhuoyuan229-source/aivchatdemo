namespace ChatApp.Core.Security;

/// <summary>
/// Runtime registry of secrets that must never reach disk logs or error text.
/// Settings register API keys here when loaded/saved; loggers and error mappers
/// redact matching values before they are written anywhere.
/// </summary>
public static class SecretRedaction
{
    private static readonly HashSet<string> Secrets = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static void Register(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return;
        lock (Gate) Secrets.Add(secret);
    }

    /// <summary>Replaces every known secret in the given text with a placeholder.</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        lock (Gate)
        {
            foreach (var secret in Secrets)
                text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return text;
    }

    public static int RegisteredCount { get { lock (Gate) return Secrets.Count; } }
}