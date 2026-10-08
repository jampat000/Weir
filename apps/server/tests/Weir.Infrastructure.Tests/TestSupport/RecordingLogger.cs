using Microsoft.Extensions.Logging;

namespace Weir.Infrastructure.Tests;

/// <summary>Captures log messages by level, so a test can wait for one instead of guessing a duration.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<string> Errors => At(LogLevel.Error);

    public IReadOnlyList<string> At(LogLevel level)
    {
        lock (_entries)
        {
            return [.. _entries.Where(entry => entry.Level == level).Select(entry => entry.Message)];
        }
    }

    /// <summary>How many messages of any level were logged.</summary>
    public int Count
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
