using ChatApp.Core.Security;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.Services;

/// <summary>
/// Writes <see cref="ILogger"/> output to a rolling daily log file under the app
/// data directory. One file per day (<c>chatapp-YYYY-MM-DD.log</c>); older files
/// are pruned after <paramref name="retention"/>. Every line is passed through
/// <see cref="SecretRedaction"/> so API keys can never reach disk.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly TimeSpan _retention;

    public RollingFileLoggerProvider(string directory, TimeSpan? retention = null)
    {
        _directory = directory;
        _retention = retention ?? TimeSpan.FromDays(7);
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal void Write(string line)
    {
        try
        {
            var safe = SecretRedaction.Redact(line);
            var path = Path.Combine(_directory, $"chatapp-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, safe + Environment.NewLine);
            Prune();
        }
        catch
        {
            // Logging must never break the application.
        }
    }

    /// <summary>Deletes daily log files older than the retention window.</summary>
    internal void Prune()
    {
        try
        {
            var cutoff = DateTime.Now - _retention;
            foreach (var file in Directory.EnumerateFiles(_directory, "chatapp-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.ToUniversalTime())
                    File.Delete(file);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}