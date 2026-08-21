using System.Text.RegularExpressions;

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
    private static readonly Regex ApiKeyPattern = new(
        @"(?<![A-Za-z0-9_-])sk-[A-Za-z0-9_-]{20,}(?![A-Za-z0-9_-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new(
        @"\b(Bearer\s+)[A-Za-z0-9._~+/=-]{12,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        text = ApiKeyPattern.Replace(text, "[REDACTED]");
        return BearerPattern.Replace(text, "$1[REDACTED]");
    }

    public static int RegisteredCount { get { lock (Gate) return Secrets.Count; } }
}
