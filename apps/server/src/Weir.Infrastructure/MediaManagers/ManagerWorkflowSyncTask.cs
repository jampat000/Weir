using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Every few minutes, brings the workflows linked to a media manager that reports its folders (Deluno) up to date with what
/// it says now, so a folder changed there reaches Weir without anyone opening Weir (<see cref="ManagerWorkflowSync"/>).
/// </summary>
public sealed class ManagerWorkflowSyncTask(ManagerWorkflowSync sync) : IPeriodicTask
{
    public string Name => "media-manager-workflow-sync";

    public string? Label => "Update workflows from media managers";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Setting up workflows from the media managers failed.";

    public Task RunOnceAsync(CancellationToken cancellationToken) => sync.SyncAllAsync(ManagerWorkflowSync.ScheduledTrigger, cancellationToken);
}
