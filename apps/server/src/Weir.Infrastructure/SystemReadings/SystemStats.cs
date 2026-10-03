namespace Weir.Infrastructure.SystemReadings;

/// <summary>
/// How the machine and Weir's own work are doing right now. A figure is null when it could not be read, for example disk
/// activity on a network share or inside a container that is not given the host's counters. The Weir and tools processor
/// figures are shares of the whole machine. <see cref="ProcessingSpeed"/> adds the running passes' speeds together: seconds
/// of media written each second. <see cref="Slots"/> is how many files Weir will process at once.
/// </summary>
public sealed record StatsNow(
    DateTimeOffset At,
    double? CpuPercent,
    int Cores,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    long? DiskReadBytesPerSecond,
    long? DiskWriteBytesPerSecond,
    double? DiskBusyPercent,
    double? WeirCpuPercent,
    long WeirMemoryBytes,
    double? ToolsCpuPercent,
    long ProcessingReadBytesPerSecond,
    long ProcessingWriteBytesPerSecond,
    double ProcessingSpeed,
    int Running,
    int Slots)
{
    /// <summary>What is known before the first reading: nothing, except the machine's core count.</summary>
    public static StatsNow Unread(DateTimeOffset at, int cores) =>
        new(at, null, cores, null, null, null, null, null, null, 0, null, 0, 0, 0, 0, 0);
}

/// <summary>One moment of the traces the System view draws.</summary>
public sealed record StatsPoint(
    DateTimeOffset At,
    double? CpuPercent,
    double? MemoryPercent,
    long? DiskReadBytesPerSecond,
    long? DiskWriteBytesPerSecond,
    long ProcessingReadBytesPerSecond,
    long ProcessingWriteBytesPerSecond,
    double ProcessingSpeed);

/// <summary>One reading, as the readings of the moment and the point to add to the traces.</summary>
public sealed record StatsSample(StatsNow Now, StatsPoint Point)
{
    public static StatsSample Of(StatsNow now) => new(
        now,
        new StatsPoint(
            now.At,
            now.CpuPercent,
            now.MemoryTotalBytes is > 0 && now.MemoryUsedBytes is { } used ? Math.Round(used * 100d / now.MemoryTotalBytes.Value, 1) : null,
            now.DiskReadBytesPerSecond,
            now.DiskWriteBytesPerSecond,
            now.ProcessingReadBytesPerSecond,
            now.ProcessingWriteBytesPerSecond,
            now.ProcessingSpeed));
}

/// <summary>What changes slowly about the machine. Each is null where the system cannot say.</summary>
public sealed record MachineFacts(string? OperatingSystem, long? UptimeSeconds, bool? RebootPending);

/// <summary>A workflow that keeps a folder on a drive, and which of its folders those are.</summary>
public sealed record WorkflowOnDrive(long Id, string Name, IReadOnlyList<string> Roles);

/// <summary>
/// One drive that holds a workflow's folders, and how it is doing. <see cref="Name"/> is the drive as a person names it: a
/// letter, a network share or a mount point. <see cref="WeirBytes"/> is what Weir's own work files take on it,
/// <see cref="KeepFreeBytes"/> the most any workflow on it wants kept free, and <see cref="FullInDays"/> when it will be full
/// at the pace it has been filling (null when it is not filling or there is too little history).
/// </summary>
public sealed record DriveReading(
    string Name,
    string Path,
    long TotalBytes,
    long FreeBytes,
    long WeirBytes,
    long KeepFreeBytes,
    double? FullInDays,
    long? ReadBytesPerSecond,
    long? WriteBytesPerSecond,
    double? BusyPercent,
    IReadOnlyList<WorkflowOnDrive> Workflows);

/// <summary>Everything the System view asks for at once: the newest reading and the points of the last window, oldest first.</summary>
public sealed record SystemStatsSnapshot(
    StatsNow Now,
    IReadOnlyList<StatsPoint> History,
    MachineFacts Machine,
    IReadOnlyList<DriveReading> Drives);
