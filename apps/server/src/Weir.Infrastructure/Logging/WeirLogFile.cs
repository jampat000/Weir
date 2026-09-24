using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Weir.Infrastructure.Logging;

/// <summary>
/// The single JSON-lines runtime log, <c>{WEIR_LOG_DIR}/weir.log</c>. Writes and the retention
/// rewrite share one lock so the file is never replaced while a handle is open, which Windows
/// does not allow.
/// </summary>
/// <remarks>
/// A read of the whole log (the Logs screen) holds a read side of <see cref="_replacing"/> instead of the write lock, so every
/// lane keeps logging while it runs (#708); only a retention rewrite waits for readers to finish.
/// </remarks>
public sealed class WeirLogFile : IDisposable
{
    private readonly Lock _lock = new();
    private readonly ReaderWriterLockSlim _replacing = new();
    private readonly TimeProvider _time;
    private FileStream? _stream;
    private bool _disposed;

    public WeirLogFile(string path, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = System.IO.Path.GetFullPath(path);
        _time = time;
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = OpenForAppend(Path);
    }

    public string Path { get; }

    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_lock)
        {
            if (_stream is null)
            {
                return;
            }

            // O_APPEND semantics: another writer (or a test) may have appended since the last write.
            _stream.Seek(0, SeekOrigin.End);
            _stream.Write(bytes);
            _stream.Flush();
        }
    }

    /// <summary>
    /// Keep only lines whose <c>timestamp</c> is within <paramref name="keepDays"/> (at least one day).
    /// Lines that are not JSON with a readable timestamp are dropped. Returns
    /// <see langword="false"/> when the file could not be rewritten; the log is left as it was.
    /// </summary>
    public bool Prune(int keepDays)
    {
        var cutoff = _time.GetUtcNow() - TimeSpan.FromDays(Math.Max(1, keepDays));
        _replacing.EnterWriteLock();
        try
        {
            return Rewrite(cutoff);
        }
        finally
        {
            _replacing.ExitWriteLock();
        }
    }

    private bool Rewrite(DateTimeOffset cutoff)
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
            string? temporary = null;
            try
            {
                if (!File.Exists(Path))
                {
                    return true;
                }

                temporary = System.IO.Path.Join(
                    System.IO.Path.GetDirectoryName(Path),
                    $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.prune");
                using (var reader = new StreamReader(Path, Encoding.UTF8))
                using (var writer = new StreamWriter(temporary, append: false, new UTF8Encoding(false)))
                {
                    writer.NewLine = "\n";
                    while (reader.ReadLine() is { } raw)
                    {
                        if (TryReadTimestamp(raw, out var at) && at >= cutoff)
                        {
                            writer.WriteLine(raw.TrimEnd('\n'));
                        }
                    }
                }

                File.Move(temporary, Path, overwrite: true);
                temporary = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (temporary is not null)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                    {
                        // Best effort: a leftover temporary file does no harm.
                    }
                }

                return false;
            }
            finally
            {
                if (!_disposed)
                {
                    _stream = OpenForAppend(Path);
                }
            }
        }
    }

    /// <summary>
    /// Read every line written so far. Writers keep appending while it reads; a prune waits until it is done, so the file is
    /// not replaced mid-read. Returns <see langword="false"/> when the file exists but could not be opened.
    /// </summary>
    public bool ReadLines(Action<string> onLine)
    {
        ArgumentNullException.ThrowIfNull(onLine);
        _replacing.EnterReadLock();
        try
        {
            FileStream stream;
            long length;
            lock (_lock)
            {
                _stream?.Flush();
                try
                {
                    stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                length = stream.Length;
            }

            using (stream)
            using (var reader = new StreamReader(new BoundedReadStream(stream, length), new UTF8Encoding(false)))
            {
                while (reader.ReadLine() is { } line)
                {
                    onLine(line);
                }
            }

            return true;
        }
        finally
        {
            _replacing.ExitReadLock();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }

        _replacing.Dispose();
    }

    internal static bool TryReadTimestamp(string raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("timestamp", out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = value.GetString()!;
            return DateTimeOffset.TryParse(
                text.EndsWith('Z') ? text[..^1] + "+00:00" : text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out timestamp);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FileStream OpenForAppend(string path) =>
        new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// The first <c>length</c> bytes of a file others keep appending to: what had been written, as whole lines, when the read
    /// began. A line still being appended is never read in half.
    /// </summary>
    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
            _remaining -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

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
