using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// #540 item 1: nothing renewed a lease, so a handler running longer than its lease could be claimed by
/// a second worker while the first was still running it (two ffmpeg processes on the same file). A
/// heartbeat now renews the lease roughly every lease/3 while the handler runs. Proven here with a real
/// SQLite file, a genuinely long-running fake handler and a second worker trying to claim in parallel, on
/// a shared <see cref="FakeTimeProvider"/> so the heartbeat's own timer, the claim's lease math and the
/// test's assertions all agree on "now" instead of racing the real clock.
/// </summary>
[Collection(SerialTestGroup.Name)]
public sealed class LeaseRenewalTests : IDisposable
{
    private const string Kind = "processing.test.long_running.v1";
    private readonly JobsTestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_job_running_longer_than_its_lease_is_never_claimed_by_another_worker()
    {
        await _db.Store.EnqueueOrGetAsync("long-running", Kind, maxAttempts: 3);

        var intruderRuns = 0;
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningHandler = new DelegateHandler(Kind, async _ =>
        {
            claimed.TrySetResult();
            await release.Task;
        });
        var intruderHandler = new DelegateHandler(Kind, _ => Interlocked.Increment(ref intruderRuns));

        using var heartbeatWaiting = new SemaphoreSlim(0);
        var runningProcessor = Processor(runningHandler, heartbeatWaiting);
        var intruderProcessor = Processor(intruderHandler);

        // A one-second lease; the handler outlives it by design, so only a working renewal keeps it safe.
        const int leaseSeconds = 1;
        var runningTask = runningProcessor.ProcessOneAsync("worker-running", leaseSeconds, _time.GetUtcNow());
        await claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForHeartbeatAsync(heartbeatWaiting);

        // Step past the original lease in heartbeat-sized increments (lease/3), so each step fires one
        // renewal, and try to claim with the intruder at the same "now" after each one. Waiting for the
        // heartbeat to be waiting again (rather than sleeping) means this step's renewal has been written
        // and the next tick's timer is set before the intruder reads the lease or the clock moves on. The
        // intruder must only ever see a lease that has been pushed out ahead of "now" - never expired.
        var intruderOutcomes = new List<JobProcessOutcome>();
        for (var step = 0; step < 6; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(leaseSeconds / 3.0));
            await WaitForHeartbeatAsync(heartbeatWaiting);
            intruderOutcomes.Add(await intruderProcessor.ProcessOneAsync("worker-intruder", leaseSeconds, _time.GetUtcNow()));
        }

        release.TrySetResult();
        var outcome = await runningTask;

        Assert.Equal(JobProcessOutcome.Processed, outcome);
        Assert.Equal(0, intruderRuns);
        Assert.All(intruderOutcomes, o => Assert.Equal(JobProcessOutcome.Idle, o));
        Assert.Equal(ProcessingJobStatus.Completed, (await _db.Store.GetAsync(1))!.Status);
    }

    [Fact]
    public async Task The_lease_is_visibly_renewed_ahead_of_its_original_expiry()
    {
        await _db.Store.EnqueueOrGetAsync("renewed", Kind, maxAttempts: 3);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(Kind, async _ =>
        {
            started.TrySetResult();
            await release.Task;
        });
        using var heartbeatWaiting = new SemaphoreSlim(0);
        var processor = Processor(handler, heartbeatWaiting);

        const int leaseSeconds = 1;
        var runTask = processor.ProcessOneAsync("worker", leaseSeconds, _time.GetUtcNow());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForHeartbeatAsync(heartbeatWaiting);
        var originalExpiry = (await _db.Store.GetAsync(1))!.LeaseExpiresAt;

        // Step forward in heartbeat-sized increments (lease/3), same as the intruder test: renewing the
        // lease only ever succeeds while it hasn't already expired, so a single jump straight past the
        // full lease would (correctly) find nothing left to renew instead of proving the heartbeat works.
        // After each step, wait until the heartbeat is waiting again: its renewal for that step is written
        // by then, and its next timer is set before the clock moves again.
        for (var step = 0; step < 3; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(leaseSeconds / 3.0));
            await WaitForHeartbeatAsync(heartbeatWaiting);
        }

        var renewedExpiry = (await _db.Store.GetAsync(1))!.LeaseExpiresAt;
        Assert.True(renewedExpiry > originalExpiry, $"expected {renewedExpiry} > {originalExpiry}");

        release.TrySetResult();
        Assert.Equal(JobProcessOutcome.Processed, await runTask);
    }

    /// <summary>
    /// Wait for the heartbeat's next "waiting for my next tick" signal. A missing signal means the heartbeat
    /// stopped renewing (it lost the lease, or crashed), which is exactly what these tests must catch.
    /// </summary>
    private static async Task WaitForHeartbeatAsync(SemaphoreSlim heartbeatWaiting) =>
        Assert.True(
            await heartbeatWaiting.WaitAsync(TimeSpan.FromSeconds(5)),
            "The lease-renewal heartbeat did not renew and wait for its next tick in time.");

    private ProcessingJobProcessor Processor(IJobHandler handler, SemaphoreSlim? heartbeatWaiting = null) =>
        new(
            new ProcessingJobStore(new SqliteDatabase(_db.DbPath, pooling: false), _time),
            new JobHandlerRegistry([handler]),
            new RecordingActivityWriter(),
            new NoUnhandledJobFailureRecorder(),
            new NoJobNotifications(),
            _time,
            NullLogger<ProcessingJobProcessor>.Instance)
        {
            LeaseRenewalWaiting = heartbeatWaiting is null ? null : () => heartbeatWaiting.Release(),
        };
}
