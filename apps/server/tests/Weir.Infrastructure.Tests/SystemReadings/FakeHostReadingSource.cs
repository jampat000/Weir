using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

/// <summary>A machine whose readings the test sets, so no test depends on the one it runs on.</summary>
internal sealed class FakeHostReadingSource : IHostReadingSource
{
    public CpuTimes? Cpu { get; set; }

    public MemoryReading? Memory { get; set; }

    public long? Uptime { get; set; } = 3600;

    public string? OperatingSystem { get; set; } = "Test OS 1";

    public bool? RebootPending { get; set; }

    public int RebootChecks { get; private set; }

    public DiskSnapshot Disks { get; set; } = DiskSnapshot.Empty;

    /// <summary>Drive roots by the folder they hold, longest folder first match wins.</summary>
    public Dictionary<string, string> RootsByFolder { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> VolumeKeysByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DriveSpace> SpaceByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);

    public CpuTimes? ReadCpuTimes() => Cpu;

    public MemoryReading? ReadMemory() => Memory;

    public long? ReadUptimeSeconds() => Uptime;

    public string? ReadOperatingSystem() => OperatingSystem;

    public bool? ReadRebootPending()
    {
        RebootChecks++;
        return RebootPending;
    }

    public DiskSnapshot ReadDisks() => Disks;

    public string? DriveRootOf(string path) =>
        RootsByFolder.Where(pair => path.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => pair.Value)
            .FirstOrDefault();

    public string? VolumeKeyOf(string driveRoot) => VolumeKeysByRoot.GetValueOrDefault(driveRoot);

    public DriveSpace? ReadSpace(string driveRoot) => SpaceByRoot.TryGetValue(driveRoot, out var space) ? space : null;
}
