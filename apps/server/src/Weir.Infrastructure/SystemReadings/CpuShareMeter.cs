namespace Weir.Infrastructure.SystemReadings;

/// <summary>
/// The share of the whole machine one process, or a set of them, used since the last reading, from their cumulative
/// processor time. Each meter keeps its own baseline, so two things measured at once need two meters.
/// </summary>
public sealed class CpuShareMeter(TimeProvider time, int cores)
{
    private DateTimeOffset _previousAt;
    private TimeSpan _previousProcessorTime;

    /// <summary>The share of all cores, 0 to 100; null for the first reading, which has nothing to compare with.</summary>
    public double? Read(TimeSpan cumulativeProcessorTime)
    {
        var now = time.GetUtcNow();
        var seconds = _previousAt == default ? 0 : (now - _previousAt).TotalSeconds;
        var used = cumulativeProcessorTime - _previousProcessorTime;
        _previousAt = now;
        _previousProcessorTime = cumulativeProcessorTime;
        return seconds > 0 ? Math.Round(Math.Clamp(used.TotalSeconds / (seconds * Math.Max(1, cores)) * 100d, 0, 100), 1) : null;
    }
}
