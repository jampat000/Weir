using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>A manager is told how many files are ahead of its file, not how many jobs of any kind.</summary>
public sealed class HandoffQueuePositionTests : IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly ProcessingJobStore _jobs;

    public HandoffQueuePositionTests() => _jobs = new ProcessingJobStore(_store.Database, _store.Clock);

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task Counts_files_ahead_and_ignores_background_scans_and_sweeps()
    {
        await _jobs.EnqueueOrGetAsync("sweep:movie", PeriodicJobKinds.WorkTempStaleSweep, "{}");
        await _jobs.EnqueueOrGetAsync("scan:1", "processing.watched_folder.remux_scan_dispatch.v1", "{}");
        await _jobs.EnqueueOrGetAsync("remux:other", "processing.file.remux_pass.v1", "{}");
        await _jobs.EnqueueOrGetAsync("clean:lib", "processing.library.clean.v1", "{}");
        var mine = await _jobs.EnqueueOrGetAsync("remux:mine", "processing.file.remux_pass.v1", "{}");

        var position = await _store.WithUnitOfWork(uow => HandoffLedgerStore.QueuePositionAsync(uow, mine), commit: false);

        // Another file's pass and a library clean are ahead of it; the sweep and the scan are not files.
        Assert.Equal(3, position);
    }
}
