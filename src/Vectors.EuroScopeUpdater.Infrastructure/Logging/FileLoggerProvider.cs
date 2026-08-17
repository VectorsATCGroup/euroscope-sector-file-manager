using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Vectors.EuroScopeUpdater.Infrastructure.Logging;

/// <summary>
/// Minimal thread-safe local file logger. Writes to <c>logs\app-yyyyMMdd.log</c>, keeps a bounded
/// number of daily files, and passes every message through <see cref="LogRedaction"/> so cookies,
/// tokens, authorization headers and signed URLs can never land in a log. No remote telemetry.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDir;
    private readonly int _keepDays;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logsDir, int keepDays = 10)
    {
        _logsDir = logsDir;
        _keepDays = keepDays;
        Directory.CreateDirectory(_logsDir);
        PruneOldFiles();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(ShortCategory(category)).Append(" — ")
            .Append(LogRedaction.Scrub(message));
        if (ex is not null)
            line.Append(" | ").Append(LogRedaction.Scrub(ex.ToString()));

        var path = Path.Combine(_logsDir, $"app-{DateTime.UtcNow:yyyyMMdd}.log");
        lock (_gate)
            File.AppendAllText(path, line.Append(Environment.NewLine).ToString());
    }

    private void PruneOldFiles()
    {
        try
        {
            var files = Directory.GetFiles(_logsDir, "app-*.log")
                .OrderByDescending(f => f).Skip(_keepDays).ToList();
            foreach (var f in files) File.Delete(f);
        }
        catch { /* logging must never throw */ }
    }

    private static string ShortCategory(string category)
    {
        var i = category.LastIndexOf('.');
        return i >= 0 ? category[(i + 1)..] : category;
    }

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Redacts sensitive material from log text. Defense in depth — nothing sensitive is passed
/// to the logger in the first place, but this guarantees it even if a caller slips.</summary>
public static class LogRedaction
{
    private static readonly (Regex Rx, string Replacement)[] Rules =
    {
        (new Regex(@"(?i)\b(authorization|cookie|set-cookie)\b\s*[:=]\s*[^\r\n]*", RegexOptions.Compiled), "$1: «redacted»"),
        (new Regex(@"(?i)\b(access_token|refresh_token|id_token|token|password|pwd|secret|api[_-]?key)\b\s*[:=]\s*[^\s&;]+", RegexOptions.Compiled), "$1=«redacted»"),
        // Strip query strings entirely from URLs — signed download URLs carry secrets there.
        (new Regex(@"(https?://[^\s?]+)\?[^\s]*", RegexOptions.Compiled), "$1?«redacted»"),
    };

    public static string Scrub(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        foreach (var (rx, repl) in Rules) input = rx.Replace(input, repl);
        return input;
    }
}
