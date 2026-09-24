using Microsoft.Data.Sqlite;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// The failure cleanup sweep on a timer. A sweep still queued or running is not
/// duplicated; a skipped Activity entry says so instead.
/// </summary>
public sealed class FailureCleanupSweepEnqueuer : IPeriodicEnqueuer
{
    private readonly ProcessingJobStore _store;
    private readonly string _scope;
    private readonly bool _killSwitch;

    public FailureCleanupSweepEnqueuer(ProcessingJobStore store, string mediaScope, TimeSpan interval, bool killSwitch)
    {
        _store = store;
        _scope = ProcessingMediaScopes.Normalize(mediaScope);
        Interval = interval;
        _killSwitch = killSwitch;
    }

    public string Name => $"failure cleanup sweep ({_scope})";

    public string JobKind => _scope == "tv" ? PeriodicJobKinds.TvFailureCleanupSweep : PeriodicJobKinds.MovieFailureCleanupSweep;

    public TimeSpan Interval { get; }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        _killSwitch
            ? Task.FromResult(false)
            : WorkTempStaleSweepEnqueuer.OperatorSettingFlagAsync(_store, "failure_cleanup_enabled", defaultValue: false, cancellationToken);

    public Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken) =>
        WorkTempStaleSweepEnqueuer.OperatorSettingIntervalAsync(_store, "failure_cleanup_interval_seconds", Interval, cancellationToken);

    public Task EnqueueOnceAsync(CancellationToken cancellationToken) =>
        _store.InTransactionAsync(
            (connection, transaction) =>
            {
                var (job, inserted) = EnqueueSweep(connection, transaction, _store, _scope, "scheduled");
                if (!inserted)
                {
                    var label = _scope == "tv" ? "TV" : "Movies";
                    var detail = new WireObject()
                        .Set("media_scope", _scope)
                        .Set("cleanup_run_status", "skipped")
                        .Set("reason", "Previous cleanup job is still queued or running.")
                        .Set("existing_job_id", job.Id)
                        .Set("result", "skipped")
                        .Set("trigger", "scheduled");
                    SqliteActivityWriter.Record(
                        connection,
                        transaction,
                        new ActivityEventDraft(ActivityEventTypes.ProcessingFailureCleanupSweepCompleted, "processing", $"Cleanup skipped for {label}", WireJsonWriter.Dumps(detail, WireJsonFormat.Compact)));
                }

                return inserted;
            },
            cancellationToken);

    internal static (ProcessingJob Job, bool Inserted) EnqueueSweep(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProcessingJobStore store,
        string scope,
        string trigger)
    {
        var jobKind = scope == "tv" ? PeriodicJobKinds.TvFailureCleanupSweep : PeriodicJobKinds.MovieFailureCleanupSweep;
        var dedupeBase = scope == "tv" ? PeriodicJobKinds.TvFailureCleanupSweepDedupeKey : PeriodicJobKinds.MovieFailureCleanupSweepDedupeKey;
        var active = ProcessingJobStore.Query(
            connection,
            transaction,
            $"SELECT {ProcessingJobStore.JobColumns} FROM jobs WHERE job_kind = @kind AND status IN (@pending, @leased) ORDER BY id ASC LIMIT 1",
            ("@kind", jobKind),
            ("@pending", ProcessingJobStatus.Pending),
            ("@leased", ProcessingJobStatus.Leased)).FirstOrDefault();
        if (active is not null)
        {
            return (active, false);
        }

        var dedupe = $"{dedupeBase}:{Guid.NewGuid():N}";
        var payload = WireJsonWriter.Dumps(new WireObject().Set("media_scope", scope).Set("trigger", trigger), WireJsonFormat.Compact);
        return (store.EnqueueOrGet(connection, transaction, dedupe, jobKind, payload, JobQueueRules.DefaultMaxAttempts, 0, 0), true);
    }
}
