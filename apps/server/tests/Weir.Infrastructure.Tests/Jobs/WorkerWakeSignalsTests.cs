using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>Idle worker slots are woken when a job is queued, and the poll stays as the fallback (#716).</summary>
public sealed class WorkerWakeSignalsTests : IDisposable
{
    private const string RemuxPass = "processing.file.remux_pass.v1";

    private readonly JobsTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void A_wake_taken_before_the_job_was_queued_is_completed_by_it()
    {
        var signals = new WorkerWakeSignals();
        var wake = signals.NextWake(0);

        signals.WakeAll();

        Assert.True(wake.IsCompletedSuccessfully);
    }

    [Fact]
    public void Each_slot_waits_on_a_signal_of_its_own()
    {
        var signals = new WorkerWakeSignals();
        var first = signals.NextWake(0);
        var second = signals.NextWake(1);

        signals.WakeAll();
        var afterwards = signals.NextWake(0);

        Assert.NotSame(first, second);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.False(afterwards.IsCompleted);
    }

    [Fact]
    public async Task Queuing_a_new_job_wakes_the_slots()
    {
        var signals = new WorkerWakeSignals();
        var store = new ProcessingJobStore(_db.Database, _db.Clock, wakeSignals: signals);
        var wake = signals.NextWake(0);

        await store.EnqueueOrGetAsync("file:one", RemuxPass);

        Assert.True(wake.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Finding_the_job_already_queued_wakes_nobody()
    {
        var signals = new WorkerWakeSignals();
        var store = new ProcessingJobStore(_db.Database, _db.Clock, wakeSignals: signals);
        await store.EnqueueOrGetAsync("file:one", RemuxPass);
        var wake = signals.NextWake(0);

        await store.EnqueueOrGetAsync("file:one", RemuxPass);

        Assert.False(wake.IsCompleted);
    }

    [Fact]
    public async Task An_idle_slot_stops_waiting_as_soon_as_it_is_woken()
    {
        var time = new FakeTimeProvider();
        var signals = new WorkerWakeSignals();
        var waiting = ProcessingWorkerService.WaitForWorkAsync(signals.NextWake(0), TimeSpan.FromMinutes(5), time, CancellationToken.None);

        signals.WakeAll();
        await waiting;

        Assert.Equal(TimeSpan.Zero, time.GetUtcNow() - time.Start);
    }

    [Fact]
    public async Task An_idle_slot_looks_again_when_the_poll_comes_round_without_a_wake()
    {
        var time = new FakeTimeProvider();
        var signals = new WorkerWakeSignals();
        var waiting = ProcessingWorkerService.WaitForWorkAsync(signals.NextWake(0), TimeSpan.FromSeconds(5), time, CancellationToken.None);
        var finishedEarly = waiting.IsCompleted;

        time.Advance(TimeSpan.FromSeconds(5));
        await waiting;

        Assert.False(finishedEarly);
    }
}
