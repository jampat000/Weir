using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Periodic tick for <c>processing.watched_folder.remux_scan_dispatch.v1</c>: a short fixed poll that
/// enqueues a scan for each enabled library once its own interval is due (see <see cref="PeriodicSchedule"/>
/// for the timing arithmetic).
/// </summary>
/// <remarks>
/// Two switches must both allow a scope before it is scheduled (#533): the global kill switch
/// <see cref="WeirOptions.ProcessingWatchedFolderRemuxScanDispatchScheduleEnabled"/>, and the per-scope
/// <c>movie_schedule_enabled</c>/<c>tv_schedule_enabled</c> the operator toggles in Settings. A scope switched
/// off is never scheduled, not even for a catch-up. Manual scans ignore both
/// (<see cref="ProcessingWatchedFolderScanDispatchEnqueue.EnqueueScanDispatchJobAsync"/>).
/// <see cref="WeirOptions.ProcessingWatchedFolderRemuxScanDispatchPeriodicEnqueueRemuxJobs"/> only decides what a
/// periodic scan does (queue remux work, or only record file state), never whether it runs.
/// </remarks>
public sealed class ProcessingWatchedFolderScanDispatchScheduleTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly ProcessingJobStore _jobStore;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessingWatchedFolderScanDispatchScheduleTask> _logger;
    private readonly Dictionary<long, DateTimeOffset> _nextRunByLibrary = [];
    private readonly ScanWakeups? _wakeups;
    private readonly ScanSettingsChanges? _scanSettingsChanges;
    private ScheduleInputs? _inputs;

    public ProcessingWatchedFolderScanDispatchScheduleTask(
        SqliteDatabase database,
        WeirOptions options,
        ProcessingJobStore jobStore,
        TimeProvider time,
        ILogger<ProcessingWatchedFolderScanDispatchScheduleTask> logger,
        ScanWakeups? wakeups = null,
        ScanSettingsChanges? scanSettingsChanges = null)
    {
        _wakeups = wakeups;
        _scanSettingsChanges = scanSettingsChanges;
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "processing-watched-folder-remux-scan-dispatch-enqueue";

    /// <summary>A short, fixed poll. Each library's due time is tracked separately, so a 1s poll is simpler
    /// and easier to test than sleeping until the nearest due library. The poll only compares due times; the
    /// libraries and switches it compares against are read far less often (<see cref="InputsRefresh"/>).</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the libraries and the scan switches are trusted before they are read again, unless a change to them is
    /// recorded first (<see cref="ScanSettingsChanges"/>). Reading them on every one-second tick was idle load for nothing (#720).
    /// </summary>
    public static readonly TimeSpan InputsRefresh = TimeSpan.FromSeconds(20);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => PeriodicSchedule.FailureCooldown;

    public string FailureMessage => "Watched-folder scheduler failed.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.ProcessingWatchedFolderRemuxScanDispatchScheduleEnabled)
        {
            // #533: the global kill switch is off. Clear every due time, so turning it back on starts
            // clean rather than firing a catch-up for every library that missed its window meanwhile.
            foreach (var id in _nextRunByLibrary.Keys)
            {
                _wakeups?.ForgetPeriodic(id);
            }

            _nextRunByLibrary.Clear();
            return;
        }

        var now = _time.GetUtcNow();
        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        var (libraries, operatorSettings) = await ReadInputsAsync(uow, now).ConfigureAwait(false);

        var activeIds = libraries.Select(l => l.Id).ToHashSet();
        foreach (var staleId in _nextRunByLibrary.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _nextRunByLibrary.Remove(staleId);
            _wakeups?.ForgetPeriodic(staleId);
        }

        foreach (var library in libraries)
        {
            var scope = ProcessingMediaScopes.Normalize(library.MediaType);
            if (!ScanDispatchScheduleGate.ScopePeriodicScanEnabled(operatorSettings.MovieScheduleEnabled, operatorSettings.TvScheduleEnabled, scope))
            {
                // #533: this scope's own "run periodic scans" switch is off. Never schedule it — not even
                // a delayed catch-up — exactly as if the library did not exist for this timer.
                _nextRunByLibrary.Remove(library.Id);
                _wakeups?.ForgetPeriodic(library.Id);
                continue;
            }

            var interval = TimeSpan.FromSeconds(PeriodicSchedule.WatchedFolderScanIntervalSeconds(library.ScanIntervalSeconds));
            var due = _nextRunByLibrary.TryGetValue(library.Id, out var existing) ? existing : now;
            // A look booked for when a held file stops being held (ScanWakeups) comes before the periodic one.
            if (now < due && _wakeups?.TakeDue(library.Id, now) == true)
            {
                due = now;
            }

            if (now < due)
            {
                continue;
            }

            var missed = PeriodicSchedule.MissedDueRunCount(now.ToUnixTimeMilliseconds() / 1000.0, due.ToUnixTimeMilliseconds() / 1000.0, interval.TotalSeconds);
            if (missed == 1)
            {
                _logger.LogWarning(
                    "Watched-folder scheduler missed {Missed} run for library {Library}; enqueueing one catch-up scan",
                    missed, library.Name);
            }
            else if (missed > 1)
            {
                _logger.LogWarning(
                    "Watched-folder scheduler missed {Missed} runs for library {Library}; enqueueing one catch-up scan",
                    missed, library.Name);
            }

            TimeSpan nextDelay;
            try
            {
                var (inserted, skip) = await ProcessingWatchedFolderScanDispatchEnqueue.TryEnqueuePeriodicAsync(
                    uow, _jobStore, library, _options.ProcessingWatchedFolderRemuxScanDispatchPeriodicEnqueueRemuxJobs).ConfigureAwait(false);
                await uow.CommitAsync().ConfigureAwait(false);
                var activeSkip = !inserted && skip is not null && skip.StartsWith("active_scan_already_queued_", StringComparison.Ordinal);
                nextDelay = activeSkip ? TimeSpan.FromSeconds(Math.Min(interval.TotalSeconds, 5.0)) : interval;
            }
#pragma warning disable CA1031 // One library's failed tick is logged and cooled down; the scheduler keeps running for the others.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _logger.LogError(exception, "Watched-folder scheduler failed for library {Library}", library.Name);
                nextDelay = PeriodicSchedule.FailureCooldown;
            }

            _nextRunByLibrary[library.Id] = now + nextDelay;
            _wakeups?.RecordNextPeriodic(library.Id, now + nextDelay);
        }
    }

    /// <summary>The enabled libraries and the scan switches, read again only when they may have changed.</summary>
    private async Task<(IReadOnlyList<ProcessingLibraryRecord> Libraries, ProcessingOperatorSettingsRecord Settings)> ReadInputsAsync(UnitOfWork uow, DateTimeOffset now)
    {
        var version = _scanSettingsChanges?.Version ?? 0;
        if (_inputs is { } known && known.LibraryVersion == version && now >= known.ReadAt && now - known.ReadAt < InputsRefresh)
        {
            return (known.Libraries, known.Settings);
        }

        var libraries = (await LibraryStore.ListAsync(uow, enabledOnly: true).ConfigureAwait(false))
            .Where(ScanDispatchScheduleGateEnabled)
            .ToList();
        var operatorSettings = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        // Release any write lock EnsureAsync took creating the singleton row on first run: the enqueue
        // below opens its own connection through ProcessingJobStore, and SQLite allows only one writer.
        await uow.CommitAsync().ConfigureAwait(false);
        _inputs = new ScheduleInputs(libraries, operatorSettings, version, now);
        return (libraries, operatorSettings);
    }

    private sealed record ScheduleInputs(
        IReadOnlyList<ProcessingLibraryRecord> Libraries,
        ProcessingOperatorSettingsRecord Settings,
        long LibraryVersion,
        DateTimeOffset ReadAt);

    private static bool ScanDispatchScheduleGateEnabled(ProcessingLibraryRecord library) =>
        ScanDispatchScheduleGate.LibraryPeriodicScanEnabled(library.Enabled);
}
