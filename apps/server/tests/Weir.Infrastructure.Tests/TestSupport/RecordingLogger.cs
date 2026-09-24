using Microsoft.Extensions.Logging;

namespace Weir.Infrastructure.Tests;

/// <summary>Captures error-level log messages, so a test can wait for one instead of guessing a duration.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel != LogLevel.Error)
        {
            return;
        }

        lock (_errors)
        {
            _errors.Add(formatter(state, exception));
        }
    }
}
