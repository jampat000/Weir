using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Http;

/// <summary>
/// <c>update-check</c>: keeps <see cref="UpdateOutlook"/> current for as long as a screen is open, so a release that comes
/// out while System is on show reaches it by itself (<see cref="DataTopics.Update"/>) instead of waiting for someone to read
/// the outlook. It asks GitHub no more often than the outlook itself would, and not at all while no stream is open.
/// </summary>
public sealed class UpdateCheckTask : IPeriodicTask
{
    private readonly UpdateOutlook _outlook;
    private readonly DataChangePublisher _changes;

    public UpdateCheckTask(UpdateOutlook outlook, DataChangePublisher changes)
    {
        _outlook = outlook ?? throw new ArgumentNullException(nameof(outlook));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    public string Name => "update-check";

    public string? Label => null;

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Weir could not check whether a newer release is out.";

    public Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_changes.HasListeners)
        {
            _outlook.CheckWhenStale();
        }

        return Task.CompletedTask;
    }
}
