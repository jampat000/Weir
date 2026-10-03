namespace Weir.Infrastructure.SystemReadings;

/// <summary>The machine's load at one moment. A figure is null when the system could not give it, or when it needs an earlier reading to compare with.</summary>
public sealed record MachineReading(
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    long? DiskReadBytesPerSecond,
    long? DiskWriteBytesPerSecond,
    double? DiskBusyPercent)
{
    public double? MemoryPercent =>
        MemoryUsedBytes is { } used && MemoryTotalBytes is > 0 ? Math.Round(used * 100d / MemoryTotalBytes.Value, 1) : null;
}

/// <summary>
/// Turns the system's cumulative counters into the machine's load: processor use, memory and disk throughput over the
/// time since the last reading. It holds that reading, so one instance serves the process and the sampler is the only
/// caller; a second caller would shorten the window for both.
/// </summary>
public sealed class MachineMeter(IHostReadingSource source, TimeProvider time)
{
    private DateTimeOffset _previousAt;
    private CpuTimes? _previousCpu;
    private DiskCounters? _previousDisks;

    public MachineReading Read()
    {
        var now = time.GetUtcNow();
        var cpu = source.ReadCpuTimes();
        var disks = source.ReadDisks().Machine;
        var memory = source.ReadMemory();
        var seconds = _previousAt == default ? 0 : (now - _previousAt).TotalSeconds;

        var activity = seconds > 0 ? HostRates.DiskActivityBetween(_previousDisks, disks, seconds) : null;
        var reading = new MachineReading(
            seconds > 0 && _previousCpu is { } before && cpu is { } current ? HostRates.CpuPercent(before, current) : null,
            memory?.Used,
            memory?.Total,
            activity?.ReadBytesPerSecond,
            activity?.WriteBytesPerSecond,
            activity?.BusyPercent);

        _previousAt = now;
        _previousCpu = cpu;
        _previousDisks = disks;
        return reading;
    }
}
