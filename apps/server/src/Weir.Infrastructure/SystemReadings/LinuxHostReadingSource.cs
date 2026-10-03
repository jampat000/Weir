namespace Weir.Infrastructure.SystemReadings;

/// <summary>The few file reads the Linux source makes, so tests can serve real contents without touching the machine they run on.</summary>
public interface IProcSource
{
    /// <summary>The file's text, or null when it does not exist or cannot be read.</summary>
    string? ReadAllText(string path);

    bool Exists(string path);
}

public sealed class ProcSource : IProcSource
{
    public string? ReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}

/// <summary>
/// The Linux and Docker readings, straight from /proc and /sys. Inside a container /proc still describes the whole host,
/// so the machine's readings are the host's, unless the container is held to a processor or memory limit: then they are
/// the cgroup's, which is all the room Weir has. What the kernel does not expose here is null: a mounted network share or
/// an overlay root has no disk counters.
/// </summary>
public sealed class LinuxHostReadingSource : IHostReadingSource
{
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const double MicrosecondsPerSecond = 1_000_000d;
    private const long MillisecondsPerSecond = 1000;

    private readonly IProcSource _files;
    private readonly TimeProvider _time;
    private CgroupCpuBaseline? _cgroupCpuBaseline;

    public LinuxHostReadingSource(IProcSource? files = null, TimeProvider? time = null)
    {
        _files = files ?? new ProcSource();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Where the cgroup's own time started counting, so its used and available time can be told apart like /proc/stat's.</summary>
    private sealed record CgroupCpuBaseline(long StartedAt, long UsageMicroseconds);

    public CpuTimes? ReadCpuTimes() =>
        ReadCgroupCpuTimes() ?? LinuxParsers.ParseCpuTimes(_files.ReadAllText("/proc/stat"));

    public MemoryReading? ReadMemory() =>
        ReadCgroupMemory() ?? LinuxParsers.ParseMemory(_files.ReadAllText("/proc/meminfo"));

    public long? ReadUptimeSeconds() => LinuxParsers.ParseUptimeSeconds(_files.ReadAllText("/proc/uptime"));

    public string? ReadOperatingSystem() => LinuxParsers.ParseOsPrettyName(_files.ReadAllText("/etc/os-release"));

    /// <summary>
    /// Debian and Ubuntu write /var/run/reboot-required when an update needs a restart. A machine without that mechanism, and
    /// any container, whose host's state it cannot see, gives null rather than "no".
    /// </summary>
    public bool? ReadRebootPending()
    {
        if (_files.Exists("/var/run/reboot-required"))
        {
            return true;
        }

        return _files.Exists("/var/lib/dpkg") && !_files.Exists("/.dockerenv") ? false : null;
    }

    public DiskSnapshot ReadDisks()
    {
        var uptimeMilliseconds = ReadUptimeSeconds() is { } seconds ? seconds * MillisecondsPerSecond : 0;
        var stats = LinuxParsers.ParseDiskStats(_files.ReadAllText("/proc/diskstats"), uptimeMilliseconds);
        if (stats.Count == 0)
        {
            return DiskSnapshot.Empty;
        }

        var wholeDisks = stats
            .Where(pair => _files.Exists($"/sys/block/{pair.Value.Name}") && LinuxParsers.IsPhysicalDiskName(pair.Value.Name))
            .Select(pair => pair.Value.Counters)
            .ToArray();

        return new DiskSnapshot(
            stats.ToDictionary(pair => pair.Key, pair => pair.Value.Counters, StringComparer.Ordinal),
            wholeDisks.Length == 0 ? null : DiskSnapshot.Sum(wholeDisks));
    }

    public string? DriveRootOf(string path) => LinuxParsers.MountOf(_files.ReadAllText("/proc/self/mountinfo"), path)?.MountPoint;

    public string? VolumeKeyOf(string driveRoot) => LinuxParsers.MountOf(_files.ReadAllText("/proc/self/mountinfo"), driveRoot)?.Device;

    public DriveSpace? ReadSpace(string driveRoot)
    {
        try
        {
            var drive = new DriveInfo(driveRoot);
            return new DriveSpace(drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The cgroup's processor time as idle and total, when a limit is set: total is the time the limit allows since the first
    /// reading, idle is what the cgroup did not use of it.
    /// </summary>
    private CpuTimes? ReadCgroupCpuTimes()
    {
        if (LinuxParsers.ParseCpuMaxCores(_files.ReadAllText($"{CgroupRoot}/cpu.max")) is not { } cores
            || LinuxParsers.ParseCpuUsageMicroseconds(_files.ReadAllText($"{CgroupRoot}/cpu.stat")) is not { } usage)
        {
            return null;
        }

        var now = _time.GetTimestamp();
        var baseline = _cgroupCpuBaseline ??= new CgroupCpuBaseline(now, usage);
        var allowed = (ulong)(_time.GetElapsedTime(baseline.StartedAt, now).TotalSeconds * MicrosecondsPerSecond * cores);
        var used = (ulong)Math.Max(0, usage - baseline.UsageMicroseconds);
        return new CpuTimes(allowed - Math.Min(allowed, used), allowed);
    }

    private MemoryReading? ReadCgroupMemory()
    {
        if (LinuxParsers.ParseBytes(_files.ReadAllText($"{CgroupRoot}/memory.max")) is not { } limit
            || LinuxParsers.ParseBytes(_files.ReadAllText($"{CgroupRoot}/memory.current")) is not { } current)
        {
            return null;
        }

        var reclaimable = LinuxParsers.ParseInactiveFileBytes(_files.ReadAllText($"{CgroupRoot}/memory.stat")) ?? 0;
        var used = Math.Clamp(current - reclaimable, 0, limit);
        return new MemoryReading(limit, limit - used);
    }
}
