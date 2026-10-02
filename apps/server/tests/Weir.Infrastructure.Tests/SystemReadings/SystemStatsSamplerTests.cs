using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

/// <summary>How often the System view's numbers are taken, and what the sampler does when something cannot be read.</summary>
public sealed class SystemStatsSamplerTests
{
    private const int Cores = 4;
    private const int DefaultSlots = 3;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeHostReadingSource _host = new();
    private readonly LiveProgressStore _progress = new();
    private readonly ActivityStreamClients _clients = new();
    private readonly FakeWorkSource _work = new();
    private readonly SystemStatsStore _store;
    private Func<OwnProcessUse> _ownProcess = () => new OwnProcessUse(TimeSpan.Zero, 1_000_000);

    public SystemStatsSamplerTests()
    {
        _store = new SystemStatsStore(_time, Cores);
        _host.RootsByFolder["D:\\"] = "D:\\";
        _host.SpaceByRoot["D:\\"] = new DriveSpace(1000, 400);
        _work.Setup = new WorkSetup([new WorkflowFolder(1, "Movies", WorkflowFolders.Output, "D:\\out", 0)], Slots: 2);
    }

    private SystemStatsSampler Sampler()
    {
        var collector = new SystemStatsCollector(
            new MachineMeter(_host, _time),
            new ProcessingThroughput(_progress, _time),
            () => TimeSpan.Zero,
            () => _ownProcess(),
            _time,
            Cores);
        var sampler = new SystemStatsSampler(
            collector,
            _store,
            new MachineFactsReader(_host, _time),
            new DriveReader(_host, new FreeSpaceForecast(_time), _time, _ => 0),
            _work,
            _clients,
            _time,
            DefaultSlots,
            NullLogger<SystemStatsSampler>.Instance);
        sampler.TakeReading();
        return sampler;
    }

    private async Task AdvanceAndRunAsync(SystemStatsSampler sampler, TimeSpan by)
    {
        _time.Advance(by);
        await sampler.RunDueWorkAsync(CancellationToken.None);
    }

    [Fact]
    public void The_first_reading_is_taken_at_once_with_the_slots_Weir_was_started_with()
    {
        Sampler();

        var snapshot = _store.Snapshot();

        Assert.Single(snapshot.History);
        Assert.Equal(DefaultSlots, snapshot.Now.Slots);
        Assert.Equal(Cores, snapshot.Now.Cores);
        Assert.Equal("Test OS 1", snapshot.Machine.OperatingSystem);
    }

    [Fact]
    public async Task With_nobody_watching_readings_come_every_ten_seconds_after_the_first_two()
    {
        var sampler = Sampler();

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        Assert.Equal(2, _store.Snapshot().History.Count);

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(8));
        Assert.Equal(2, _store.Snapshot().History.Count);

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(2));
        Assert.Equal(3, _store.Snapshot().History.Count);
    }

    [Fact]
    public async Task While_a_stream_is_open_a_reading_comes_every_second()
    {
        var sampler = Sampler();
        using var streamOpen = _clients.Open();

        for (var second = 1; second <= 5; second++)
        {
            await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(6, _store.Snapshot().History.Count);
    }

    [Fact]
    public async Task Once_the_last_stream_closes_the_pace_drops_back_to_every_ten_seconds()
    {
        var sampler = Sampler();
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        var streamOpen = _clients.Open();
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        streamOpen.Dispose();

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));

        Assert.Equal(3, _store.Snapshot().History.Count);
    }

    [Fact]
    public async Task Drives_and_slots_are_read_when_the_loop_first_runs_and_then_every_thirty_seconds()
    {
        var sampler = Sampler();

        await sampler.RunDueWorkAsync(CancellationToken.None);
        Assert.Equal(1, _work.Reads);
        var drive = _store.Snapshot().Drives.Single();
        Assert.Equal("D:", drive.Name);
        Assert.Equal(["Movies"], drive.Workflows.Select(workflow => workflow.Name));

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(29));
        Assert.Equal(1, _work.Reads);

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        Assert.Equal(2, _work.Reads);
    }

    [Fact]
    public async Task The_slots_read_from_the_settings_are_the_ones_the_next_reading_reports()
    {
        var sampler = Sampler();
        using var streamOpen = _clients.Open();

        await sampler.RunDueWorkAsync(CancellationToken.None);
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));

        Assert.Equal(2, _store.Snapshot().Now.Slots);
    }

    [Fact]
    public async Task When_the_workflows_cannot_be_read_the_drives_stay_as_they_were_and_readings_carry_on()
    {
        var sampler = Sampler();
        using var streamOpen = _clients.Open();
        await sampler.RunDueWorkAsync(CancellationToken.None);
        var drivesBefore = _store.Snapshot().Drives;
        _work.Failure = new SqliteException("database is locked", 5);

        await AdvanceAndRunAsync(sampler, SystemStatsSampler.DriveInterval);

        var snapshot = _store.Snapshot();
        Assert.Equal(drivesBefore, snapshot.Drives);
        Assert.Equal(2, snapshot.History.Count);
    }

    [Fact]
    public async Task A_reading_that_fails_leaves_a_gap_and_the_next_one_is_tried_again()
    {
        var sampler = Sampler();
        using var streamOpen = _clients.Open();
        _ownProcess = () => throw new InvalidOperationException("the process went away");

        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        Assert.Single(_store.Snapshot().History);

        _ownProcess = () => new OwnProcessUse(TimeSpan.Zero, 1);
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));
        Assert.Equal(2, _store.Snapshot().History.Count);
    }

    [Fact]
    public async Task Weirs_own_figures_are_the_processors_share_and_its_memory()
    {
        var processorTime = TimeSpan.Zero;
        _ownProcess = () => new OwnProcessUse(processorTime, 123_456);
        var sampler = Sampler();
        using var streamOpen = _clients.Open();

        processorTime = TimeSpan.FromSeconds(1);
        await AdvanceAndRunAsync(sampler, TimeSpan.FromSeconds(1));

        var now = _store.Snapshot().Now;
        Assert.Equal(25.0, now.WeirCpuPercent);
        Assert.Equal(123_456, now.WeirMemoryBytes);
    }

    private sealed class FakeWorkSource : IWorkSource
    {
        public WorkSetup Setup { get; set; } = new([], 1);

        public Exception? Failure { get; set; }

        public int Reads { get; private set; }

        public Task<WorkSetup> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Failure is { } failure ? Task.FromException<WorkSetup>(failure) : Task.FromResult(Setup);
        }
    }
}
