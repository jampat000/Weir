using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>
/// "Scheduled scan and clean" runs on its schedule (James, 23 Sep 2026). Before the timer existed the switch was saved
/// and shown, and nothing read it.
/// </summary>
public sealed class LibraryModeScheduleTaskTests : IDisposable
{
    private readonly MediaManagerFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private LibraryModeScheduleTask Timer() =>
        new(_fixture.Store.Database, _fixture.Jobs, _fixture.Store.Clock, NullLogger<LibraryModeScheduleTask>.Instance);

    private async Task<long> LibraryAsync(bool scheduleOn = true, bool withFolder = true, string name = "Films")
    {
        var id = Convert.ToInt64(await _fixture.Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES (@name, 'movie', '/downloads/' || @name, '/output/' || @name, '/work/' || @name, 1) RETURNING id",
            ("@name", name))), CultureInfo.InvariantCulture);
        IReadOnlyList<string> folders = withFolder ? ["/library/films"] : [];
        await _fixture.Db(async uow => { await LibrarySettingsStore.SetAsync(uow, id, new LibrarySettings(folders, scheduleOn)); return true; });
        return id;
    }

    private Task<List<(long Id, string Payload)>> ScansAsync(long library) =>
        _fixture.Db(uow => uow.QueryAsync(
            "SELECT id, payload_json FROM jobs WHERE job_kind = @kind AND json_extract(payload_json, '$.library_id') = @library ORDER BY id",
            reader => (reader.GetInt64(0), reader.GetString(1)),
            ("@kind", LibraryModeJobKinds.ScanKind),
            ("@library", library)), commit: false);

    private Task<bool> FinishAsync(long jobId) =>
        _fixture.Db(async uow => { await uow.ExecuteAsync("UPDATE jobs SET status = 'completed' WHERE id = @id", ("@id", jobId)); return true; });

    private Task<DateTimeOffset?> NextRunAsync(long library) =>
        _fixture.Db(
            async uow => await LibraryModeScheduling.NextRunAsync(
                uow,
                (await Weir.Infrastructure.Processing.LibraryStore.GetAsync(uow, library))!,
                await LibrarySettingsStore.GetAsync(uow, library),
                _fixture.Store.Clock.GetUtcNow()),
            commit: false);

    [Fact]
    public void It_is_hosted_under_its_own_name()
    {
        Assert.Equal("processing-library-mode-schedule", Timer().Name);
        Assert.True(Timer().RunAtStart);
    }

    [Fact]
    public async Task A_library_with_the_schedule_on_is_scanned_on_schedule_and_then_once_a_day()
    {
        var library = await LibraryAsync();
        var start = _fixture.Store.Clock.GetUtcNow();

        await Timer().RunOnceAsync(CancellationToken.None);

        var first = Assert.Single(await ScansAsync(library));
        Assert.Contains("\"trigger\":\"schedule\"", first.Payload, StringComparison.Ordinal);
        Assert.Equal(start, await _fixture.Db(uow => LibraryScanStore.LastScheduledRunAtAsync(uow, library), commit: false));
        Assert.Equal(start.AddDays(1), await NextRunAsync(library));

        // While that scan is waiting nothing more is queued, and once it is done the next is a day away.
        await Timer().RunOnceAsync(CancellationToken.None);
        await FinishAsync(first.Id);
        _fixture.Store.Clock.Set(start.AddHours(23));
        await Timer().RunOnceAsync(CancellationToken.None);
        Assert.Single(await ScansAsync(library));

        // A day later, a few seconds late: the second run keeps the time it was due, so the day does not creep.
        _fixture.Store.Clock.Set(start.AddDays(1).AddSeconds(20));
        await Timer().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, (await ScansAsync(library)).Count);
        Assert.Equal(start.AddDays(1), await _fixture.Db(uow => LibraryScanStore.LastScheduledRunAtAsync(uow, library), commit: false));
    }

    [Fact]
    public async Task A_missed_day_is_run_once_and_the_day_starts_again_from_then()
    {
        var library = await LibraryAsync();
        var start = _fixture.Store.Clock.GetUtcNow();
        await Timer().RunOnceAsync(CancellationToken.None);
        await FinishAsync(Assert.Single(await ScansAsync(library)).Id);

        // Weir was off for three days.
        var back = start.AddDays(4).AddHours(5);
        _fixture.Store.Clock.Set(back);
        await Timer().RunOnceAsync(CancellationToken.None);
        await Timer().RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, (await ScansAsync(library)).Count);
        Assert.Equal(back, await _fixture.Db(uow => LibraryScanStore.LastScheduledRunAtAsync(uow, library), commit: false));
    }

    [Fact]
    public async Task A_scan_someone_asked_for_is_left_to_finish_first()
    {
        var library = await LibraryAsync();
        await _fixture.Store.WithUnitOfWork(uow => LibraryScanStore.RequestScanAsync(uow, _fixture.Jobs, library, "manual"));

        await Timer().RunOnceAsync(CancellationToken.None);

        Assert.Single(await ScansAsync(library));
        Assert.Null(await _fixture.Db(uow => LibraryScanStore.LastScheduledRunAtAsync(uow, library), commit: false));
    }

    [Fact]
    public async Task Nothing_runs_with_the_schedule_off_or_no_library_folders()
    {
        var off = await LibraryAsync(scheduleOn: false);
        var noFolders = await LibraryAsync(withFolder: false, name: "Shows");

        await Timer().RunOnceAsync(CancellationToken.None);

        Assert.Empty(await ScansAsync(off));
        Assert.Empty(await ScansAsync(noFolders));
        Assert.Null(await NextRunAsync(off));
        Assert.Null(await NextRunAsync(noFolders));
    }

    [Fact]
    public async Task A_library_waits_for_its_window_to_open()
    {
        var library = await LibraryAsync();
        // The fixture's clock is Thursday 15 January 2026, 10:00 UTC; this library may only work from 02:00 to 04:00.
        var grid = Weir.Core.Jobs.ScheduleGrid.FromDaysAndTimes("Mon,Tue,Wed,Thu,Fri,Sat,Sun", "02:00", "04:00");
        await _fixture.Db(async uow =>
        {
            await uow.ExecuteAsync("UPDATE libraries SET schedule_enabled = 1, schedule_grid = @grid WHERE id = @id", ("@grid", grid), ("@id", library));
            return true;
        });

        await Timer().RunOnceAsync(CancellationToken.None);

        Assert.Empty(await ScansAsync(library));
        Assert.Equal(new DateTimeOffset(2026, 1, 16, 2, 0, 0, TimeSpan.Zero), await NextRunAsync(library));
    }
}
