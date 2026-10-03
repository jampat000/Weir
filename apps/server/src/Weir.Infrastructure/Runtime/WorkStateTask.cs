using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// <c>update-work-state</c>: while an update waits to install, tells the tray every <see cref="Every"/> whether Weir
/// has file work running or able to start, so the tray can install the update once nothing has been happening for a
/// while (#875). The answer goes in a file in the data folder, which only the account running Weir can open; no
/// route exposes it, whatever address Weir listens on. With no update waiting nothing is written.
/// </summary>
public sealed class WorkStateTask(UpdateFiles files, ProcessingJobStore jobs, JobHandlerRegistry handlers, TimeProvider time) : IPeriodicTask
{
    /// <summary>How often the answer is refreshed. The tray treats an answer several times older than this as no answer.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    public string Name => "update-work-state";

    public string? Label => null;

    public TimeSpan Interval => Every;

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => TimeSpan.FromMinutes(5);

    public string FailureMessage => "Weir could not tell the tray whether it is idle, so a downloaded update cannot install itself until it can.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!files.HasDownloadedUpdate())
        {
            return;
        }

        var now = time.GetUtcNow();
        var busy = await jobs.HasFileWorkAsync(now, WorkLanes.FilesLaneKinds(handlers), cancellationToken).ConfigureAwait(false);
        files.WriteWorkState(busy, now);
    }
}
