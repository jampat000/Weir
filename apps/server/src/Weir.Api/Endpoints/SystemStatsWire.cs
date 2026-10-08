using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Api.Endpoints;

/// <summary>The System view's readings as the API's JSON: <c>GET /system/stats</c> and the <c>system.stats</c> frame.</summary>
public static class SystemStatsWire
{
    /// <summary>How often a stream gets a reading while a browser is watching, as the response tells clients.</summary>
    public const int IntervalMilliseconds = 1000;

    public static WireObject Snapshot(SystemStatsSnapshot snapshot) => new WireObject()
        .Set("interval_ms", IntervalMilliseconds)
        .Set("window_s", (long)SystemStatsStore.Window.TotalSeconds)
        .Set("now", Now(snapshot.Now))
        .Set("history", new WireArray(snapshot.History.Select(point => (WireValue)Point(point))))
        .Set("machine", Machine(snapshot.Machine))
        .Set("drives", Drives(snapshot.Drives));

    public static WireObject Now(StatsNow now) => new WireObject()
        .Set("at", At(now.At))
        .Set("cpu_percent", Number(now.CpuPercent))
        .Set("cores", now.Cores)
        .Set("memory_used_bytes", now.MemoryUsedBytes)
        .Set("memory_total_bytes", now.MemoryTotalBytes)
        .Set("disk_read_bytes_per_sec", now.DiskReadBytesPerSecond)
        .Set("disk_write_bytes_per_sec", now.DiskWriteBytesPerSecond)
        .Set("disk_busy_percent", Number(now.DiskBusyPercent))
        .Set("weir_cpu_percent", Number(now.WeirCpuPercent))
        .Set("weir_memory_bytes", now.WeirMemoryBytes)
        .Set("tools_cpu_percent", Number(now.ToolsCpuPercent))
        .Set("processing_read_bytes_per_sec", now.ProcessingReadBytesPerSecond)
        .Set("processing_write_bytes_per_sec", now.ProcessingWriteBytesPerSecond)
        .Set("processing_speed", now.ProcessingSpeed)
        .Set("running", now.Running)
        .Set("slots", now.Slots);

    public static WireObject Point(StatsPoint point) => new WireObject()
        .Set("at", At(point.At))
        .Set("cpu_percent", Number(point.CpuPercent))
        .Set("memory_percent", Number(point.MemoryPercent))
        .Set("disk_read_bytes_per_sec", point.DiskReadBytesPerSecond)
        .Set("disk_write_bytes_per_sec", point.DiskWriteBytesPerSecond)
        .Set("processing_read_bytes_per_sec", point.ProcessingReadBytesPerSecond)
        .Set("processing_write_bytes_per_sec", point.ProcessingWriteBytesPerSecond)
        .Set("processing_speed", point.ProcessingSpeed);

    public static WireObject Machine(MachineFacts machine) => new WireObject()
        .Set("os", machine.OperatingSystem)
        .Set("uptime_seconds", machine.UptimeSeconds)
        .Set("reboot_pending", machine.RebootPending is { } pending ? WireValue.Of(pending) : WireValue.Null);

    public static WireArray Drives(IReadOnlyList<DriveReading> drives) => new(drives.Select(drive => (WireValue)Drive(drive)));

    private static WireObject Drive(DriveReading drive) => new WireObject()
        .Set("name", drive.Name)
        .Set("path", drive.Path)
        .Set("total_bytes", drive.TotalBytes)
        .Set("free_bytes", drive.FreeBytes)
        .Set("weir_bytes", drive.WeirBytes)
        .Set("keep_free_bytes", drive.KeepFreeBytes)
        .Set("full_in_days", Number(drive.FullInDays))
        .Set("read_bytes_per_sec", drive.ReadBytesPerSecond)
        .Set("write_bytes_per_sec", drive.WriteBytesPerSecond)
        .Set("busy_percent", Number(drive.BusyPercent))
        .Set("workflows", new WireArray(drive.Workflows.Select(workflow => (WireValue)new WireObject()
            .Set("id", workflow.Id)
            .Set("name", workflow.Name)
            .Set("roles", new WireArray(workflow.Roles.Select(role => (WireValue)WireValue.Of(role)))))));

    private static WireValue Number(double? value) => value is { } number ? WireValue.Of(number) : WireValue.Null;

    private static string At(DateTimeOffset at) => Timestamp.FromDateTimeOffset(at.ToUniversalTime()).ToWireText();
}
