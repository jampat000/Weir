using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Settings;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// In-process worker handler for <c>processing.watched_folder.remux_scan_dispatch.v1</c>: scan the library's
/// watched folder, decide each candidate file's state, and, when asked, enqueue
/// <c>processing.file.remux_pass.v1</c> jobs for it.
/// </summary>
/// <remarks>
/// <para>Per-file attribution to a specific media-manager queue row (path/id/title-year matching a manager's
/// raw JSON to a candidate) is <see cref="ManagerQueueSignals.AttributedRowsForFile"/>, applied through
/// <see cref="WatchedFileDispatch"/> exactly as the "why held" diagnostic applies it.</para>
/// <para>A scan is an independent lane: it reads what it needs, walks and decides with no transaction open, and writes in
/// short batches (<see cref="WatchedFolderScanBatch"/>), so no other lane ever waits on it for the write lock (#708).</para>
/// </remarks>
public sealed class ProcessingWatchedFolderScanDispatchJobHandler : IJobHandler
{
    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;
    private readonly WeirOptions _options;
    private readonly ProcessingJobStore _jobStore;
    private readonly MediaManagerConnectionService _managerConnections;
    private readonly SuiteSettingsStore _suiteSettings;

    private readonly ScanWakeups? _wakeups;

    public ProcessingWatchedFolderScanDispatchJobHandler(
        SqliteDatabase database,
        TimeProvider time,
        WeirOptions options,
        ProcessingJobStore jobStore,
        MediaManagerConnectionService managerConnections,
        SuiteSettingsStore suiteSettings,
        ScanWakeups? wakeups = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _managerConnections = managerConnections ?? throw new ArgumentNullException(nameof(managerConnections));
        _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));
        _wakeups = wakeups;
    }

    public string JobKind => ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch;

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var body = ParsePayload(context.PayloadJson);
        var request = new ScanRequest(
            body.Get("library_id") is WireInteger idValue && idValue.Value > 0 ? (long)idValue.Value : null,
            ProcessingMediaScopes.Normalize(body.Get("media_scope") is WireString scopeStr ? scopeStr.Value : null),
            ScanDispatchJobPayload.NormalizeTrigger(body.Get("scan_trigger") is WireString triggerStr ? triggerStr.Value : "manual"),
            body.Get("enqueue_remux_jobs") is WireValue v && v.IsTruthy);

        var (scan, budget) = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        var candidates = WatchedFolderListing.Candidates(
            scan.Paths.WatchedFolder,
            scan.Rules.MediaExtensions.Count > 0 ? scan.Rules.MediaExtensions : null,
            scan.Rules.ExcludeMarkers,
            scan.Rules.ExcludeHidden,
            scan.Rules.TopLevelOnly);

        var reads = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (reads.ConfigureAwait(false))
        {
            var lookups = await WatchedFolderScanLookups.ReadAsync(reads, scan).ConfigureAwait(false);
            var run = new WatchedFolderScanRun(_database, _jobStore, scan, lookups, reads, new ScanPassRequest(context.Id, request.Trigger, budget));
            await run.RunAsync(candidates.Entries, cancellationToken).ConfigureAwait(false);

            // #645: a file that left the watched folder before Weir finished with it stops being listed. Only while the watched
            // folder itself can be read, so an unmounted share never empties the list.
            if (Directory.Exists(scan.Paths.WatchedFolder))
            {
                await VanishedFiles.ForgetAsync(_database, scan.Library.Id, scan.Paths.WatchedFolder, scan.MediaScope, scan.Now, "scan", cancellationToken)
                    .ConfigureAwait(false);
            }

            // A second after the first hold ends, so the look finds it over. A pass booked for later (#646) carries its own
            // start time and needs no look; this is for files the scan itself holds.
            if (run.EarliestHoldEnds is { } firstEnds)
            {
                _wakeups?.Request(scan.Library.Id, firstEnds + TimeSpan.FromSeconds(1));
            }

            await run.RemoveRejectedFilesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record ScanRequest(long? LibraryId, string MediaScope, string Trigger, bool EnqueueRemuxJobs);

    /// <summary>
    /// The library, its folders and rules, what its managers are downloading and whether it may start work now. Reads only,
    /// apart from the settings rows a fresh database creates, and it leaves no transaction open.
    /// </summary>
    private async Task<(WatchedFolderScan Scan, RunnerBudget Budget)> PrepareAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var library = request.LibraryId is { } wantedId ? await LibraryStore.GetAsync(uow, wantedId).ConfigureAwait(false) : null;
            library ??= await LibraryStore.SeededForScopeAsync(uow, request.MediaScope).ConfigureAwait(false);
            if (library is null)
            {
                var label = request.MediaScope == ProcessingMediaScopes.Tv ? "TV" : "Movies";
                throw new InvalidOperationException($"No library covers {label}. Add one in Processing → Libraries, then queue this work again.");
            }

            var (paths, pathError) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, _options.WeirHome);
            if (paths is null)
            {
                throw new InvalidOperationException(pathError ?? "Path settings are incomplete for this scan.");
            }

            // Manager queue signals: ask every manager linked to this library, and note who did not answer. Always the
            // library's own linked connections, even when that is none: an empty selector means "ask nobody", not "fall back
            // to every connection for the scope".
            var connectionIds = await LibraryStore.ManagerConnectionIdsAsync(uow, library.Id).ConfigureAwait(false);
            var signals = await _managerConnections.CollectQueueSignalsAsync(uow, request.MediaScope, connectionIds, cancellationToken).ConfigureAwait(false);

            var operatorSettings = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
            var suite = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);

            var rules = LibraryAdmissionRules.For(library);
            var scan = new WatchedFolderScan(
                library,
                request.MediaScope,
                paths,
                rules,
                signals,
                AdmissionWindow(library, suite, now),
                Math.Max(operatorSettings.MinFileAgeSeconds, rules.MinFileAgeSeconds),
                request.EnqueueRemuxJobs,
                now);
            var budget = RunnerBudget.FromSettings(
                operatorSettings.RunnerCapacity, operatorSettings.RunnerCostSd, operatorSettings.RunnerCost720P,
                operatorSettings.RunnerCost1080P, operatorSettings.RunnerCost4K, operatorSettings.RunnerCostUndetermined);
            return (scan, budget);
        }
    }

    private static ScanAdmissionWindow AdmissionWindow(ProcessingLibraryRecord library, SuiteSettingsRecord suite, DateTimeOffset now)
    {
        var pause = PauseState.Resolve(suite, now.UtcDateTime);
        var timezoneName = string.IsNullOrWhiteSpace(suite.AppTimezone) ? "UTC" : suite.AppTimezone.Trim();
        var admissionSnapshot = new LibraryAdmissionSnapshot(
            library.Id, library.Enabled, library.ScheduleEnabled, library.ScheduleGrid, library.ScheduleHoursLimited,
            library.ScheduleDays, library.ScheduleStart, library.ScheduleEnd, library.MaxConcurrentFiles);
        var inWindow = WorkAdmissionRules.LibraryWindowOpen(admissionSnapshot, timezoneName, now);
        return new ScanAdmissionWindow(
            inWindow,
            pause.Paused ? pause.Reason : null,
            pause.Paused && pause.PausedUntil is { } until ? new DateTimeOffset(until.AsUtc) : null,
            inWindow ? null : WorkAdmissionRules.LibraryWindowReopensAt(admissionSnapshot, timezoneName, now));
    }

    private static WireObject ParsePayload(string? payloadJson)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new WireObject();
        }

        WireValue data;
        try
        {
            data = WireJsonParser.Parse(raw);
        }
        catch (WireJsonDecodeException exception)
        {
            throw new ArgumentException("watched-folder remux scan dispatch payload must be a JSON object", exception);
        }

        if (data is not WireObject dict)
        {
            throw new ArgumentException("watched-folder remux scan dispatch payload must be a JSON object");
        }

        return dict;
    }
}
