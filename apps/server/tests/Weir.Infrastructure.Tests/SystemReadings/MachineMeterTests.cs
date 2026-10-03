using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class MachineMeterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeHostReadingSource _host = new();

    private static DiskSnapshot OneVolume(DiskCounters counters) =>
        DiskSnapshot.FromVolumes(new Dictionary<string, DiskCounters> { ["C:"] = counters });

    [Fact]
    public void The_first_reading_has_memory_but_no_rates_because_rates_need_two_readings()
    {
        _host.Memory = new MemoryReading(1000, 400);
        _host.Cpu = new CpuTimes(100, 200);

        var reading = new MachineMeter(_host, _time).Read();

        Assert.Equal(new MachineReading(null, 600, 1000, null, null, null), reading);
        Assert.Equal(60.0, reading.MemoryPercent);
    }

    [Fact]
    public void The_next_reading_gives_processor_use_and_disk_throughput_over_the_time_between()
    {
        var meter = new MachineMeter(_host, _time);
        _host.Cpu = new CpuTimes(0, 0);
        _host.Disks = OneVolume(new DiskCounters(0, 0, 0, 0));
        meter.Read();

        _time.Advance(TimeSpan.FromSeconds(2));
        _host.Cpu = new CpuTimes(Idle: 600, Total: 1000);
        _host.Disks = OneVolume(new DiskCounters(4000, 2000, 1500, 2000));
        var reading = meter.Read();

        Assert.Equal(40.0, reading.CpuPercent);
        Assert.Equal(2000, reading.DiskReadBytesPerSecond);
        Assert.Equal(1000, reading.DiskWriteBytesPerSecond);
        Assert.Equal(25.0, reading.DiskBusyPercent);
    }

    [Fact]
    public void A_machine_that_gives_no_counters_gives_nothing_rather_than_a_guess()
    {
        var meter = new MachineMeter(_host, _time);
        meter.Read();
        _time.Advance(TimeSpan.FromSeconds(1));

        var reading = meter.Read();

        Assert.Equal(new MachineReading(null, null, null, null, null, null), reading);
        Assert.Null(reading.MemoryPercent);
    }
}
