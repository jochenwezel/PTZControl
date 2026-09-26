using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PTZControlServer;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly object _sync = new();
    private DateOnly _lastCleanupDate;

    public DailyFileLoggerProvider(string directory, LogLevel minimumLevel)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (level < _minimumLevel || _minimumLevel == LogLevel.None)
            return;

        var now = DateTimeOffset.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}";
        if (eventId.Id != 0)
            line += $" ({eventId.Id})";
        line += $": {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(GetLogPath(now), line + Environment.NewLine);
                CleanupOldLogs(DateOnly.FromDateTime(now.LocalDateTime));
            }
            catch
            {
                // Diagnostics must never interrupt API or camera operations.
            }
        }
    }

    private string GetLogPath(DateTimeOffset timestamp) =>
        Path.Combine(_directory, $"PTZControlServer-{timestamp:yyyy-MM-dd}.log");

    private void CleanupOldLogs(DateOnly today)
    {
        if (_lastCleanupDate == today)
            return;
        _lastCleanupDate = today;
        var cutoff = today.AddDays(-13);
        foreach (var path in Directory.EnumerateFiles(_directory, "PTZControlServer-????-??-??.log"))
        {
            var dateText = Path.GetFileNameWithoutExtension(path)["PTZControlServer-".Length..];
            if (DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff)
                File.Delete(path);
        }
    }

    private sealed class DailyFileLogger(DailyFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel && provider._minimumLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}
