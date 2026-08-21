using ChatApp.Core.Security;
using ChatApp.UI.Services;
using Microsoft.Extensions.Logging;

namespace ChatApp.Tests;

/// <summary>
/// Coverage for the plan-3.6 rolling file logger: redaction before disk writes,
/// daily file naming and retention pruning.
/// </summary>
public class RollingFileLoggerTests
{
    [Fact]
    public void UnknownApiKeyAndBearerTokenPatternsAreRedacted()
    {
        const string apiKey = "sk-abcdefghijklmnopqrstuvwxyz123456";
        const string bearer = "abcdefghijklmnopqrstuvwxyz.ABCDEFGHIJKLMNOP";

        var redacted = SecretRedaction.Redact($"key={apiKey}; Authorization: Bearer {bearer}");

        Assert.DoesNotContain(apiKey, redacted);
        Assert.DoesNotContain(bearer, redacted);
        Assert.Equal(2, redacted.Split("[REDACTED]", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void UserFacingErrorsClassifyHttpFailuresAndRedactGenericMessages()
    {
        Assert.Contains("鉴权失败", UserFacingError.FromException(
            new HttpRequestException("provider payload", null, System.Net.HttpStatusCode.Unauthorized)));

        const string secret = "sk-abcdefghijklmnopqrstuvwxyz123456";
        var message = UserFacingError.FromException(new InvalidOperationException($"bad key {secret}"));
        Assert.DoesNotContain(secret, message);
        Assert.Contains("[REDACTED]", message);
    }

    [Fact]
    public void LogLinesAreRedactedBeforeReachingDisk()
    {
        var secret = $"sk-{Guid.NewGuid():N}";
        SecretRedaction.Register(secret);
        var dir = NewTempDir();
        try
        {
            using var provider = new RollingFileLoggerProvider(dir);
            var logger = provider.CreateLogger("ChatApp.Tests");
            logger.LogInformation("connecting with key={Key}", secret);

            var content = File.ReadAllText(Path.Combine(dir, $"chatapp-{DateTime.Now:yyyy-MM-dd}.log"));

            Assert.DoesNotContain(secret, content);
            Assert.Contains("[REDACTED]", content);
            Assert.Contains("[Information]", content);
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void WritesToDailyRollingFile()
    {
        var dir = NewTempDir();
        try
        {
            using var provider = new RollingFileLoggerProvider(dir);
            provider.CreateLogger("test").LogError("boom");

            var expected = $"chatapp-{DateTime.Now:yyyy-MM-dd}.log";
            Assert.True(File.Exists(Path.Combine(dir, expected)), $"expected {expected} to exist");
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void FilesOlderThanRetentionArePrunedOnWrite()
    {
        var dir = NewTempDir();
        try
        {
            using var provider = new RollingFileLoggerProvider(dir, TimeSpan.FromDays(7));
            var stale = Path.Combine(dir, "chatapp-2020-01-01.log");
            File.WriteAllText(stale, "old");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-8));

            provider.CreateLogger("test").LogWarning("fresh write triggers prune");

            Assert.False(File.Exists(stale), "stale daily file should have been pruned");
            Assert.True(File.Exists(Path.Combine(dir, $"chatapp-{DateTime.Now:yyyy-MM-dd}.log")));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"chatapp-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
