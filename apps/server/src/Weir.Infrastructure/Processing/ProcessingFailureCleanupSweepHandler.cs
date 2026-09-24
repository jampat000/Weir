using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// Worker handler for a Pass 4 failure-cleanup sweep job. One
/// subclass is registered per scope (<see cref="MovieFailureCleanupSweepHandler"/>, <see cref="TvFailureCleanupSweepHandler"/>),
/// since <c>processing.movie_failure_cleanup_sweep.v1</c> and <c>processing.tv_failure_cleanup_sweep.v1</c> are distinct job
/// kinds with a fixed default scope each — kept as distinct types (rather than one class parameterized by string) so
/// dependency injection can tell the two registrations apart.
/// </summary>
public abstract class ProcessingFailureCleanupSweepHandler : IJobHandler
{
    private readonly ProcessingFailureCleanupSweep _sweep;
    private readonly string _defaultScope;

    protected ProcessingFailureCleanupSweepHandler(ProcessingFailureCleanupSweep sweep, string jobKind, string defaultScope)
    {
        _sweep = sweep ?? throw new ArgumentNullException(nameof(sweep));
        JobKind = jobKind ?? throw new ArgumentNullException(nameof(jobKind));
        _defaultScope = defaultScope ?? throw new ArgumentNullException(nameof(defaultScope));
    }

    public string JobKind { get; }

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (mediaScope, trigger) = ParsePayload(context.PayloadJson, _defaultScope);

        var database = _sweep.Database;
        var startedDetail = new WireObject().Set("job_id", context.Id).Set("media_scope", mediaScope).Set("cleanup_run_status", "started");
        await LockedWrites.RunAsync(
            database,
            uow => ProcessingFailureCleanupActivity.RecordSweepStartedAsync(uow, mediaScope, startedDetail, trigger),
            _sweep.Logger,
            "failure cleanup sweep started",
            cancellationToken).ConfigureAwait(false);

        var result = await _sweep.RunForScopeAsync(mediaScope, cancellationToken).ConfigureAwait(false);
        result.Set("job_id", context.Id);

        await LockedWrites.RunAsync(
            database,
            uow => ProcessingFailureCleanupActivity.RecordSweepCompletedAsync(uow, mediaScope, result, trigger),
            _sweep.Logger,
            "failure cleanup sweep completed",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The scope and provenance trigger, tolerant of a missing or malformed payload.</summary>
    private static (string MediaScope, string? Trigger) ParsePayload(string? payloadJson, string defaultScope)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return (defaultScope, null);
        }

        WireValue parsed;
        try
        {
            parsed = WireJsonParser.Parse(payloadJson);
        }
        catch (WireJsonDecodeException)
        {
            return (defaultScope, null);
        }

        if (parsed is not WireObject data)
        {
            return (defaultScope, null);
        }

        var scope = data.Get("media_scope") is WireString scopeValue && string.Equals(WireStrings.Strip(scopeValue.Value), "tv", StringComparison.OrdinalIgnoreCase)
            ? "tv"
            : "movie";
        var trigger = data.Get("trigger") is WireString triggerValue ? triggerValue.Value : null;
        return (scope, trigger);
    }
}

/// <summary>Handles <c>processing.movie_failure_cleanup_sweep.v1</c>.</summary>
public sealed class MovieFailureCleanupSweepHandler : ProcessingFailureCleanupSweepHandler
{
    public MovieFailureCleanupSweepHandler(ProcessingFailureCleanupSweep sweep)
        : base(sweep, PeriodicJobKinds.MovieFailureCleanupSweep, "movie")
    {
    }
}

/// <summary>Handles <c>processing.tv_failure_cleanup_sweep.v1</c>.</summary>
public sealed class TvFailureCleanupSweepHandler : ProcessingFailureCleanupSweepHandler
{
    public TvFailureCleanupSweepHandler(ProcessingFailureCleanupSweep sweep)
        : base(sweep, PeriodicJobKinds.TvFailureCleanupSweep, "tv")
    {
    }
}
