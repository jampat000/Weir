using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class MachineFactsReaderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeHostReadingSource _host = new() { OperatingSystem = "Windows 11 Pro", Uptime = 7200, RebootPending = true };

    [Fact]
    public void The_facts_are_the_systems_name_its_uptime_and_whether_it_wants_a_restart()
    {
        var facts = new MachineFactsReader(_host, _time).Read();

        Assert.Equal(new MachineFacts("Windows 11 Pro", 7200, true), facts);
    }

    [Fact]
    public void Uptime_is_read_every_time_but_the_restart_check_only_once_a_minute()
    {
        var reader = new MachineFactsReader(_host, _time);
        reader.Read();

        _time.Advance(TimeSpan.FromSeconds(30));
        _host.Uptime = 7230;
        _host.RebootPending = false;
        var within = reader.Read();

        _time.Advance(TimeSpan.FromSeconds(30));
        var after = reader.Read();

        Assert.Equal(7230, within.UptimeSeconds);
        Assert.True(within.RebootPending);
        Assert.False(after.RebootPending);
        Assert.Equal(2, _host.RebootChecks);
    }

    [Fact]
    public void A_system_that_cannot_say_gives_nulls()
    {
        _host.OperatingSystem = null;
        _host.Uptime = null;
        _host.RebootPending = null;

        Assert.Equal(new MachineFacts(null, null, null), new MachineFactsReader(_host, _time).Read());
    }
}
