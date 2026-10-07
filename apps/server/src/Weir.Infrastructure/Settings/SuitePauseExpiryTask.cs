using Weir.Core.Time;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Settings;

/// <summary>
/// <c>suite-pause-expiry</c>: lifts a timed pause once its time has run out, and records that in Activity, so a pause never
/// ends unseen. Work does not wait for this task: the claim reads the pause against the clock itself.
/// </summary>
public sealed class SuitePauseExpiryTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly SuitePauseService _pause;
    private readonly TimeProvider _time;

    public SuitePauseExpiryTask(SqliteDatabase database, SuitePauseService pause, TimeProvider time)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _pause = pause ?? throw new ArgumentNullException(nameof(pause));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public string Name => "suite-pause-expiry";

    public string? Label => null;

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Weir could not check whether a timed pause has run out.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            await _pause.CurrentAsync(uow, Timestamp.UtcNow(_time)).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }
}
