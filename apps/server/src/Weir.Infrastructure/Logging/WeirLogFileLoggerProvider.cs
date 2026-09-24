using Microsoft.Extensions.Logging;

namespace Weir.Infrastructure.Logging;

/// <summary>Writes every log entry at or above the configured level to <see cref="WeirLogFile"/>.</summary>
public sealed class WeirLogFileLoggerProvider : ILoggerProvider
{
    private readonly WeirLogFile _file;
    private readonly TimeProvider _time;
    private readonly LogLevel _minimumLevel;

    public WeirLogFileLoggerProvider(WeirLogFile file, TimeProvider time, LogLevel minimumLevel)
    {
        _file = file;
        _time = time;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // The file is owned by whoever created it.
    }

    private sealed class FileLogger(WeirLogFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider._file.WriteLine(PythonLogFormat.JsonLine(
                provider._time.GetUtcNow(),
                logLevel,
                category,
                formatter(state, exception),
                exception,
                LogContext.RequestId,
                LogContext.JobId));
        }
    }
}
