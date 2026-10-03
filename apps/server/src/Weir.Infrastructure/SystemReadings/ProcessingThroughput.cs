using Weir.Infrastructure.Processing;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>What Weir's own work is doing: files being processed, how fast they are read and written, and how fast the tools run through them.</summary>
public sealed record ProcessingReading(int Running, long ReadBytesPerSecond, long WriteBytesPerSecond, double Speed);

/// <summary>
/// Weir's own live work, taken from what every running pass reports to <see cref="LiveProgressStore"/>. Rates come from how
/// far each pass has got since the previous reading; a pass that began in between counts from its start, and one that ended
/// is not counted for the last stretch. The reader holds the previous reading, so the sampler is its only caller.
/// </summary>
public sealed class ProcessingThroughput(LiveProgressStore progress, TimeProvider time)
{
    private DateTimeOffset _previousAt;
    private IReadOnlyDictionary<string, LiveProgress> _previous = new Dictionary<string, LiveProgress>(StringComparer.Ordinal);

    public ProcessingReading Read()
    {
        var now = time.GetUtcNow();
        var current = progress.Snapshot();
        var seconds = _previousAt == default ? 0 : (now - _previousAt).TotalSeconds;

        long read = 0, written = 0;
        foreach (var (path, pass) in current)
        {
            _previous.TryGetValue(path, out var before);
            read += Advance(before?.BytesRead, pass.BytesRead);
            written += Advance(before?.BytesWritten, pass.BytesWritten);
        }

        _previousAt = now;
        _previous = current;
        return new ProcessingReading(
            current.Count,
            seconds > 0 ? (long)Math.Round(read / seconds) : 0,
            seconds > 0 ? (long)Math.Round(written / seconds) : 0,
            current.Values.Sum(pass => pass.SpeedMultiple ?? 0));
    }

    /// <summary>The bytes a pass has moved since the last reading: all it reports if it is new, nothing if its count went backwards.</summary>
    private static long Advance(long? before, long? now) => now is { } current ? Math.Max(0, current - (before ?? 0)) : 0;
}
