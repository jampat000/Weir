using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// The unclaimed hand-back cleanup on a timer (#652): one row per scope, deduped per scope, like the work file sweep.
/// Enabled by <c>operator_settings.unclaimed_handback_cleanup_enabled</c>, which is off until a person switches it on; the
/// interval is the one saved in Settings › Cleanup, or six hours.
/// </summary>
public sealed class UnclaimedHandbackCleanupEnqueuer : IPeriodicEnqueuer
{
    private readonly ProcessingJobStore _store;
    private readonly string _scope;

    public UnclaimedHandbackCleanupEnqueuer(ProcessingJobStore store, string mediaScope)
    {
        _store = store;
        _scope = ProcessingMediaScopes.Normalize(mediaScope);
    }

    public string Name => $"unclaimed hand-back cleanup ({_scope})";

    public string JobKind => PeriodicJobKinds.UnclaimedHandbackCleanup;

    public TimeSpan Interval => TimeSpan.FromSeconds(HandbackRules.DefaultUnclaimedIntervalSeconds);

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        WorkTempStaleSweepEnqueuer.OperatorSettingFlagAsync(_store, "unclaimed_handback_cleanup_enabled", defaultValue: false, cancellationToken);

    public Task<TimeSpan> IntervalAsync(CancellationToken cancellationToken) =>
        WorkTempStaleSweepEnqueuer.OperatorSettingIntervalAsync(_store, "unclaimed_handback_cleanup_interval_seconds", Interval, cancellationToken);

    public Task EnqueueOnceAsync(CancellationToken cancellationToken) =>
        _store.EnqueueOrGetAsync(
            _scope == "tv" ? PeriodicJobKinds.UnclaimedHandbackCleanupDedupeKeyTv : PeriodicJobKinds.UnclaimedHandbackCleanupDedupeKeyMovie,
            PeriodicJobKinds.UnclaimedHandbackCleanup,
            WireJsonWriter.Dumps(new WireObject().Set("media_scope", _scope).Set("trigger", "scheduled"), WireJsonFormat.Compact),
            cancellationToken: cancellationToken);
}
