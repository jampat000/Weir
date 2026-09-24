using Weir.Core.Activity;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Activity;

/// <summary>
/// <c>activity-latest-poll</c>: a safety net for the live Activity stream (#734). A write this process makes
/// itself tells <see cref="ActivityLatestNotifier"/> the instant its transaction commits, so the common case
/// needs no polling. A write this process never sees commit — <c>weir recover</c>'s own connection, a restored
/// backup, or any other direct edit of the database file — would otherwise sit unseen by every open stream
/// until a client reloads. One shared poll, not one per open stream, catches that within a few seconds.
/// </summary>
public sealed class ActivityLatestPollTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly ActivityLatestNotifier _notifier;

    public ActivityLatestPollTask(SqliteDatabase database, ActivityLatestNotifier notifier)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    public string Name => "activity-latest-poll";

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Activity latest-id poll failed";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var latest = await ActivityHistoryStore.LatestIdAsync(_database, cancellationToken).ConfigureAwait(false);
        if (latest is { } id && id != _notifier.Snapshot().LatestId)
        {
            _notifier.Notify(id);
        }
    }
}
