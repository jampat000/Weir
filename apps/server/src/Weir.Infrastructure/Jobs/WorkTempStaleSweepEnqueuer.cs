using Weir.Core.Json;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// The work file sweep on a timer: one row per scope, deduped per scope.
/// Enabled by <c>operator_settings.work_temp_stale_sweep_enabled</c>; an explicit
/// <c>WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED=0</c> is a kill switch.
/// </summary>
public sealed class WorkTempStaleSweepEnqueuer : IPeriodicEnqueuer
{
    private readonly ProcessingJobStore _store;
    private readonly string _scope;
    private readonly bool _killSwitch;

    public WorkTempStaleSweepEnqueuer(ProcessingJobStore store, string mediaScope, TimeSpan interval, bool killSwitch)
    {
        _store = store;
        _scope = ProcessingMediaScopes.Normalize(mediaScope);
        Interval = interval;
        _killSwitch = killSwitch;
    }

    public string Name => $"work temp stale sweep ({_scope})";

    public string JobKind => PeriodicJobKinds.WorkTempStaleSweep;

    public TimeSpan Interval { get; }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        _killSwitch ? Task.FromResult(false) : OperatorSettingFlagAsync(_store, "work_temp_stale_sweep_enabled", defaultValue: true, cancellationToken);

    public Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken) =>
        OperatorSettingIntervalAsync(_store, "work_temp_stale_sweep_interval_seconds", Interval, cancellationToken);

    public Task EnqueueOnceAsync(CancellationToken cancellationToken) =>
        _store.EnqueueOrGetAsync(
            _scope == "tv" ? PeriodicJobKinds.WorkTempStaleSweepDedupeKeyTv : PeriodicJobKinds.WorkTempStaleSweepDedupeKeyMovie,
            PeriodicJobKinds.WorkTempStaleSweep,
            WireJsonWriter.Dumps(new WireObject().Set("media_scope", _scope).Set("trigger", "scheduled"), WireJsonFormat.Compact),
            cancellationToken: cancellationToken);

    /// <summary>An interval column of the operator settings row (seconds), or <paramref name="fallback"/> when it is not set.</summary>
    internal static Task<TimeSpan> OperatorSettingIntervalAsync(ProcessingJobStore store, string column, TimeSpan fallback, CancellationToken cancellationToken) =>
        store.ReadAsync(
            (connection, transaction) =>
            {
                var value = ProcessingJobStore.Scalar(connection, transaction, $"SELECT {column} FROM operator_settings WHERE id = 1");
                return value is long seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : fallback;
            },
            cancellationToken);

    /// <summary>A boolean column of the operator settings row, or <paramref name="defaultValue"/> when the row or value is missing.</summary>
    internal static Task<bool> OperatorSettingFlagAsync(ProcessingJobStore store, string column, bool defaultValue, CancellationToken cancellationToken) =>
        store.ReadAsync(
            (connection, transaction) =>
            {
                var value = ProcessingJobStore.Scalar(connection, transaction, $"SELECT {column} FROM operator_settings WHERE id = 1");
                return value is null or DBNull ? defaultValue : WorkAdmissionReader.Bool(value);
            },
            cancellationToken);
}
