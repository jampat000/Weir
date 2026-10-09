using Microsoft.Extensions.Logging;

namespace Weir.Infrastructure.Tests;

/// <summary>Captures every message at every level, so a test can wait for the one it is about instead of guessing a duration.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _messages = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public bool Logged(LogLevel level, string text) =>
        Messages.Any(message => message.Level == level && message.Message.Contains(text, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add((logLevel, formatter(state, exception)));
        }
    }
}
