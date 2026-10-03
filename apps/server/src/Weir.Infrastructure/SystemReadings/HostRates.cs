namespace Weir.Infrastructure.SystemReadings;

/// <summary>Turns two cumulative readings into a rate. Pure functions: whoever holds the earlier reading owns the baseline.</summary>
public static class HostRates
{
    /// <summary>The share of the window the processors were not idle, 0 to 100; null when the counters did not advance.</summary>
    public static double? CpuPercent(CpuTimes before, CpuTimes now)
    {
        if (now.Total <= before.Total || now.Idle < before.Idle)
        {
            return null;
        }

        var total = (double)(now.Total - before.Total);
        var idle = (double)(now.Idle - before.Idle);
        return Math.Round(Math.Clamp((1d - idle / total) * 100d, 0, 100), 1);
    }

    /// <summary>A rate from two cumulative counters; null when the counter went backwards (it wrapped or restarted).</summary>
    public static long? PerSecond(long before, long now, double seconds) =>
        seconds > 0 && now >= before ? (long)Math.Round((now - before) / seconds) : null;

    /// <summary>The share of the window a disk was busy: the time it was not idle against the time that passed.</summary>
    public static double? BusyPercent(DiskCounters before, DiskCounters now)
    {
        var query = now.QueryTime - before.QueryTime;
        var idle = now.IdleTime - before.IdleTime;
        return query > 0 && idle >= 0
            ? Math.Round(Math.Clamp((1d - (double)idle / query) * 100d, 0, 100), 1)
            : null;
    }

    /// <summary>What one disk or volume did over the window; null when either reading is missing.</summary>
    public static DiskActivity? DiskActivityBetween(DiskCounters? before, DiskCounters? now, double seconds)
    {
        if (before is null || now is null)
        {
            return null;
        }

        return new DiskActivity(
            PerSecond(before.BytesRead, now.BytesRead, seconds),
            PerSecond(before.BytesWritten, now.BytesWritten, seconds),
            BusyPercent(before, now));
    }
}

/// <summary>A disk's throughput and busy share over a window; each part is null when it could not be worked out.</summary>
public sealed record DiskActivity(long? ReadBytesPerSecond, long? WriteBytesPerSecond, double? BusyPercent);
