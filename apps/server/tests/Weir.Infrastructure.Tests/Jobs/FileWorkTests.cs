using System.Text.Json;
using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// What counts as file work for the question "is Weir idle?" (#875): a pass that is running, or queued and able to start.
/// Queued work that is not going to start, and upkeep, does not count.
/// </summary>
public sealed class FileWorkTests : IDisposable
{
    private const string Remux = "processing.file.remux_pass.v1";
    private const string Scan = "processing.library.scan.v1";
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 14, 0, 0, TimeSpan.Zero);
    private static readonly string Never = new('0', ScheduleGrid.SlotsPerWeek);
    private readonly JobsTestDatabase _db = new();
    private readonly ClaimableKinds _fileKinds = WorkLanes.FilesLaneKinds(new JobHandlerRegistry(
    [
        new DelegateHandler(Remux, _ => { }),
        new DelegateHandler(Scan, _ => { }),
    ]));

    public FileWorkTests()
    {
        _db.SeedSuiteSettings();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task An_empty_queue_is_not_file_work()
    {
        Assert.False(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_running_pass_is_file_work()
    {
        await RunAPassAsync(_db.AddLibrary());

        Assert.True(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_running_pass_is_file_work_after_its_schedule_window_closes()
    {
        var library = _db.AddLibrary();
        await RunAPassAsync(library);
        _db.Execute("UPDATE libraries SET schedule_grid = @grid", ("@grid", Never));

        Assert.True(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_that_could_start_now_is_file_work()
    {
        await QueueAsync(Remux, _db.AddLibrary());

        Assert.True(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_waiting_for_its_schedule_window_is_not_file_work()
    {
        await QueueAsync(Remux, _db.AddLibrary(scheduleGrid: Never));

        Assert.False(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_for_a_switched_off_workflow_is_not_file_work()
    {
        await QueueAsync(Remux, _db.AddLibrary(enabled: false));

        Assert.False(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_while_processing_is_paused_is_not_file_work()
    {
        await QueueAsync(Remux, _db.AddLibrary());
        _db.Pause(scanWhilePaused: true);

        Assert.False(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_held_back_until_later_is_not_file_work()
    {
        var library = _db.AddLibrary();
        var job = await QueueAsync(Remux, library);
        _db.Execute("UPDATE jobs SET not_before = @later WHERE id = @id", ("@later", TimestampColumns.Orm(Now.AddHours(1))), ("@id", job.Id));

        Assert.False(await HasFileWorkAsync());
    }

    [Fact]
    public async Task A_queued_pass_becomes_file_work_when_its_window_opens()
    {
        var slots = Never.ToCharArray();
        for (var slot = 0; slot < ScheduleGrid.SlotsPerDay; slot++)
        {
            slots[(2 * ScheduleGrid.SlotsPerDay) + slot] = '1';
        }

        await QueueAsync(Remux, _db.AddLibrary(scheduleGrid: new string(slots)));

        Assert.True(await HasFileWorkAsync(Now));
        Assert.False(await HasFileWorkAsync(Now.AddDays(1)));
    }

    [Fact]
    public async Task A_running_or_queued_scan_is_not_file_work()
    {
        var library = _db.AddLibrary();
        await QueueAsync(Scan, library, "queued");
        _db.InsertRawJob(
            "running-scan",
            Scan,
            status: ProcessingJobStatus.Leased,
            leaseOwner: "u0",
            leaseExpiresAt: TimestampColumns.Orm(Now.AddHours(1)),
            payloadJson: $"{{\"library_id\": {library}}}");

        Assert.False(await HasFileWorkAsync());
    }

    private Task<bool> HasFileWorkAsync(DateTimeOffset? now = null) => _db.Store.HasFileWorkAsync(now ?? Now, _fileKinds);

    private async Task RunAPassAsync(long library)
    {
        await QueueAsync(Remux, library);
        Assert.NotNull(await _db.Store.ClaimNextAdmittedAsync("w1", Now.AddHours(1), Now, _fileKinds));
    }

    private Task<ProcessingJob> QueueAsync(string kind, long libraryId, string key = "k")
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["media_scope"] = "movie", ["library_id"] = libraryId });
        return _db.Store.EnqueueOrGetAsync($"{kind}:{key}", kind, payload);
    }
}
