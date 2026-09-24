using Microsoft.Data.Sqlite;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// Reads of the job queue and the operator settings the workers poll never take the write lock, so they neither wait for a
/// writer nor make writers wait for them (#708). Each test holds the write lock on another connection while it reads; a read
/// that asked for the lock would wait out the busy timeout and fail.
/// </summary>
public sealed class JobReadsTests : IDisposable
{
    private readonly JobsTestDatabase _db = new(keepSeedRows: true);

    public void Dispose() => _db.Dispose();

    private static SqliteTransaction HoldTheWriteLock(SqliteConnection connection) =>
        connection.BeginTransaction(deferred: false);

    [Fact]
    public async Task A_job_is_read_while_another_lane_holds_the_write_lock()
    {
        var job = await _db.Store.EnqueueOrGetAsync("read-me", "processing.test.read.v1");
        using var writer = _db.Database.Open();
        using var held = HoldTheWriteLock(writer);

        Assert.Equal(job.Id, (await _db.Store.GetAsync(job.Id))!.Id);
    }

    [Fact]
    public async Task The_queue_is_listed_while_another_lane_holds_the_write_lock()
    {
        await _db.Store.EnqueueOrGetAsync("list-me", "processing.test.read.v1");
        using var writer = _db.Database.Open();
        using var held = HoldTheWriteLock(writer);

        Assert.Contains(await _db.Store.ListAsync(), job => job.DedupeKey == "list-me");
    }

    [Fact]
    public async Task A_periodic_familys_switch_is_read_while_another_lane_holds_the_write_lock()
    {
        _db.Execute("UPDATE operator_settings SET work_temp_stale_sweep_enabled = 0");
        using var writer = _db.Database.Open();
        using var held = HoldTheWriteLock(writer);

        Assert.False(await WorkTempStaleSweepEnqueuer.OperatorSettingFlagAsync(_db.Store, "work_temp_stale_sweep_enabled", defaultValue: true, CancellationToken.None));
    }
}
