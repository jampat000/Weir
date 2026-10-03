namespace Weir.Infrastructure.SystemReadings;

/// <summary>Cumulative processor time since boot, in any consistent unit.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Total);

/// <summary>Physical memory, in bytes.</summary>
public readonly record struct MemoryReading(long Total, long Available)
{
    public long Used => Math.Max(0, Total - Available);
}

/// <summary>A drive's size and what is left on it, in bytes.</summary>
public readonly record struct DriveSpace(long Total, long Free);

/// <summary>
/// One volume's cumulative disk counters. <see cref="IdleTime"/> and <see cref="QueryTime"/> share a unit, so busy is
/// the part of the window that was not idle.
/// </summary>
public sealed record DiskCounters(long BytesRead, long BytesWritten, long IdleTime, long QueryTime);

/// <summary>Every local volume's counters, and the machine's disks added together (each physical disk once).</summary>
public sealed record DiskSnapshot(IReadOnlyDictionary<string, DiskCounters> Volumes, DiskCounters? Machine)
{
    public static readonly DiskSnapshot Empty = new(new Dictionary<string, DiskCounters>(), null);

    public static DiskSnapshot FromVolumes(IReadOnlyDictionary<string, DiskCounters> volumes) =>
        volumes.Count == 0 ? Empty : new DiskSnapshot(volumes, Sum(volumes.Values));

    public static DiskCounters Sum(IEnumerable<DiskCounters> counters)
    {
        long read = 0, written = 0, idle = 0, query = 0;
        foreach (var counter in counters)
        {
            read += counter.BytesRead;
            written += counter.BytesWritten;
            idle += counter.IdleTime;
            query += counter.QueryTime;
        }

        return new DiskCounters(read, written, idle, query);
    }
}

/// <summary>
/// The operating system's raw readings. Everything is cumulative or instantaneous and nothing here computes a rate:
/// <see cref="HostRates"/> does that between two readings. A reading the system cannot give is null, never a guess.
/// There is one implementation for Windows (kernel calls and the registry) and one for Linux and Docker (/proc and
/// /sys), and tests use fakes.
/// </summary>
public interface IHostReadingSource
{
    CpuTimes? ReadCpuTimes();

    MemoryReading? ReadMemory();

    long? ReadUptimeSeconds();

    /// <summary>The operating system's name as a person would say it, such as <c>Windows 11 Pro</c>.</summary>
    string? ReadOperatingSystem();

    /// <summary>Whether the system is waiting for a restart; null where it cannot tell.</summary>
    bool? ReadRebootPending();

    DiskSnapshot ReadDisks();

    /// <summary>
    /// The drive that holds <paramref name="path"/>: a drive root such as <c>D:\</c> or <c>\\nas\media</c> on Windows, the
    /// mount point on Linux. Null for a path that cannot be placed.
    /// </summary>
    string? DriveRootOf(string path);

    /// <summary>The key in <see cref="DiskSnapshot.Volumes"/> for the local volume under <paramref name="driveRoot"/>; null for a network share.</summary>
    string? VolumeKeyOf(string driveRoot);

    /// <summary>Size and free space of the drive under <paramref name="driveRoot"/>, network shares included; null when it cannot be read.</summary>
    DriveSpace? ReadSpace(string driveRoot);
}

/// <summary>Which source reads the operating system Weir is running on.</summary>
public static class HostReadingSources
{
    public static IHostReadingSource ForThisMachine() =>
        OperatingSystem.IsWindows() ? new WindowsHostReadingSource() : new LinuxHostReadingSource();
}
