using System.Globalization;
using Weir.Core.Activity;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Settings;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// In-process worker handler for <c>processing.watched_folder.remux_scan_dispatch.v1</c>: scan the library's
/// watched folder, decide each candidate file's state, and, when asked, enqueue
/// <c>processing.file.remux_pass.v1</c> jobs for it.
/// </summary>
/// <remarks>
/// Per-file attribution to a specific media-manager queue row (path/id/title-year matching a manager's
/// raw JSON to a candidate) is <see cref="ManagerQueueSignals.AttributedRowsForFile"/>, applied through
/// <see cref="WatchedFileDispatch"/> exactly as the "why held" diagnostic applies it.
/// </remarks>
public sealed class ProcessingWatchedFolderScanDispatchJobHandler : IJobHandler
{
    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;
    private readonly WeirOptions _options;
    private readonly ProcessingJobStore _jobStore;
    private readonly MediaManagerConnectionService _managerConnections;

    private readonly ScanWakeups? _wakeups;

    public ProcessingWatchedFolderScanDispatchJobHandler(
        SqliteDatabase database,
        TimeProvider time,
        WeirOptions options,
        ProcessingJobStore jobStore,
        MediaManagerConnectionService managerConnections,
        ScanWakeups? wakeups = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _managerConnections = managerConnections ?? throw new ArgumentNullException(nameof(managerConnections));
        _wakeups = wakeups;
    }

    public string JobKind => ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch;

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var body = ParsePayload(context.PayloadJson);
        var enqueueRemuxJobs = body.Get("enqueue_remux_jobs") is PyJson v && v.IsTruthy;
        var scanTriggerRaw = body.Get("scan_trigger") is PyStr triggerStr ? triggerStr.Value : "manual";
        var scanTrigger = ScanDispatchJobPayload.NormalizeTrigger(scanTriggerRaw);
        var mediaScope = ProcessingMediaScopes.Normalize(body.Get("media_scope") is PyStr scopeStr ? scopeStr.Value : null);
        var libraryId = body.Get("library_id") is PyInt idValue && idValue.Value > 0 ? (long)idValue.Value : (long?)null;

        var now = _time.GetUtcNow();

        await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);

        var library = libraryId is { } wantedId ? await LibraryStore.GetAsync(uow, wantedId).ConfigureAwait(false) : null;
        library ??= await LibraryStore.SeededForScopeAsync(uow, mediaScope).ConfigureAwait(false);
        if (library is null)
        {
            var label = mediaScope == ProcessingMediaScopes.Tv ? "TV" : "Movies";
            throw new InvalidOperationException($"No library covers {label}. Add one in Processing → Libraries, then queue this work again.");
        }

        var (runtime, pathError) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, _options.WeirHome);
        if (runtime is null)
        {
            throw new InvalidOperationException(pathError ?? "Path settings are incomplete for this scan.");
        }

        // Manager queue signals: ask every manager linked to this library, and note who did not answer.
        // Always the library's own linked connections, even when that is none: an empty selector means
        // "ask nobody", not "fall back to every connection for the scope".
        var connectionIds = await LibraryStore.ManagerConnectionIdsAsync(uow, library.Id).ConfigureAwait(false);
        var signals = await _managerConnections.CollectQueueSignalsAsync(uow, mediaScope, connectionIds, cancellationToken).ConfigureAwait(false);

        var operatorSettings = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        var suite = await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        var pause = PauseState.Resolve(suite, now.UtcDateTime);
        var timezoneName = string.IsNullOrWhiteSpace(suite.AppTimezone) ? "UTC" : suite.AppTimezone.Trim();
        var pauseReason = pause.Paused ? pause.Reason : null;
        var pauseUntil = pause.Paused && pause.PausedUntil is { } pu ? new DateTimeOffset(pu.AsUtc) : (DateTimeOffset?)null;

        var admissionSnapshot = new LibraryAdmissionSnapshot(
            library.Id, library.Enabled, library.ScheduleEnabled, library.ScheduleGrid, library.ScheduleHoursLimited,
            library.ScheduleDays, library.ScheduleStart, library.ScheduleEnd, library.MaxConcurrentFiles);
        var inWindow = WorkAdmissionRules.LibraryWindowOpen(admissionSnapshot, timezoneName, now);
        var reopensAt = inWindow ? null : WorkAdmissionRules.LibraryWindowReopensAt(admissionSnapshot, timezoneName, now);

        var rules = LibraryAdmissionRules.For(library);
        var effectiveMinAgeSeconds = Math.Max(operatorSettings.MinFileAgeSeconds, rules.MinFileAgeSeconds);

        var candidates = WatchedFolderScanOps.IterWatchedFolderMediaCandidates(
            runtime.WatchedFolder,
            rules.MediaExtensions.Count > 0 ? rules.MediaExtensions : null,
            rules.ExcludeMarkers,
            rules.ExcludeHidden,
            rules.TopLevelOnly);

        var relThisRun = new HashSet<string>(StringComparer.Ordinal);
        var pendingRejectedCleanups = new List<(string RelativePath, string FilePath, string Reason, string Action)>();

        // The earliest moment a file held by this scan stops being held; the next look is booked for then.
        DateTimeOffset? earliestHoldEnds = null;

        foreach (var filePath in candidates.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath))
            {
                // A successful Movies cleanup can remove a release folder while this scan still holds a
                // candidate for one of its extras. Do not turn a stale entry into new state or a job.
                continue;
            }

            var rel = WatchedFolderScanOps.RelativePosixPathUnderWatched(runtime.WatchedFolder, filePath);
            // The candidate anchor is this file's own stem (not the library's name), so anchor matching only
            // ever compares one release title to another.
            var candidate = new FileAnchorCandidate(Path.GetFileNameWithoutExtension(filePath));
            var attributedRows = ManagerQueueSignals.AttributedRowsForFile(signals, mediaScope, Path.GetFullPath(filePath));
            var outcome = WatchedFileDispatch.Evaluate(attributedRows, candidate);

            var observedSize = FileSizeBytes(filePath);
            var previous = await FileStateStore.ExistingFileRowAsync(uow, library.Id, rel).ConfigureAwait(false);

            // The file the last successful pass cleaned — same size, same modification time, both recorded by that pass — is
            // finished: note that it was seen and move on, so it is never queued again. That holds for a library that keeps
            // originals (#627) and for one that removes them but had to leave this one in place (#644): TV season cleanup skips
            // a season while an episode is still queued or its manager cannot be asked, and a Movies removal can be
            // interrupted. Otherwise every scan would clean such a file again and hand it back again. A new or replaced file
            // at the same path differs in one of the two and is processed as usual. A movie whose removal was interrupted
            // goes on below, so that removal can still be finished.
            var keepsOriginals = !library.RemoveOriginalAfterSuccess;
            var alreadyCleaned = ProcessedSourceRules.IsSameCleanedFile(previous, observedSize, ModifiedTimeNs(filePath));
            if (alreadyCleaned && (keepsOriginals || mediaScope != ProcessingMediaScopes.Movie))
            {
                await FileStateStore.TouchLastSeenAsync(uow, previous!.Id, now).ConfigureAwait(false);
                continue;
            }

            // With a fingerprint on record, it alone decides whether this is the cleaned file; the activity-history
            // check below (path and size only) would mistake a same-size replacement for it.
            var fingerprintDecides = keepsOriginals && ProcessedSourceRules.HasFingerprint(previous);

            if (previous is not null && previous.SizeBytes != observedSize)
            {
                // A changed source is a new processing opportunity: do not carry a failure/quarantine
                // counter from the old bytes into the new file.
                previous = previous with
                {
                    FailureClass = null,
                    FailureAttempts = 0,
                    NextRetryAt = null,
                    Status = previous.Status is ProcessingFileStatuses.ProcessingFailed or ProcessingFileStatuses.OnHold
                        or ProcessingFileStatuses.PassedThrough or ProcessingFileStatuses.Rejected or ProcessingFileStatuses.Cancelled
                        ? ProcessingFileStatuses.Unprocessed
                        : previous.Status,
                };
                await ResetFailureBookkeepingAsync(uow, previous).ConfigureAwait(false);
            }

            if (previous is not null && previous.SizeBytes == observedSize)
            {
                if (previous.Status is ProcessingFileStatuses.PassedThrough or ProcessingFileStatuses.Rejected or ProcessingFileStatuses.Cancelled)
                {
                    await FileStateStore.TouchLastSeenAsync(uow, previous.Id, now).ConfigureAwait(false);
                    continue;
                }

                if (previous.Status == ProcessingFileStatuses.ProcessingFailed)
                {
                    var retryAt = previous.NextRetryAt?.AsUtc;
                    if (retryAt is { } r && r <= now.UtcDateTime)
                    {
                        // Automatic retry: enqueue the way a fresh candidate is, not through RequeueStore, whose
                        // reset (failure_attempts back to 0, backoff cleared) is for a person's "retry now". Used
                        // here it would wipe the counter every scan, so a file could never fail often enough to be
                        // quarantined; only RecordFailureAsync, when the attempt's outcome comes back, touches
                        // these fields. Release uow's write lock before ProcessingJobStore opens its own
                        // connection, or the two connections deadlock.
                        await uow.CommitAsync().ConfigureAwait(false);
                        await EnqueueRemuxPassAsync(uow, context, library, mediaScope, rel, scanTrigger, previous).ConfigureAwait(false);
                        continue;
                    }

                    await FileStateStore.TouchLastSeenAsync(uow, previous.Id, now).ConfigureAwait(false);
                    continue;
                }

                if (previous.Status == ProcessingFileStatuses.OnHold && previous.FailureAttempts >= RetryPolicy.QuarantineAfterFailures)
                {
                    await FileStateStore.TouchLastSeenAsync(uow, previous.Id, now).ConfigureAwait(false);
                    continue;
                }
            }

            var settling = FileSettling.ObserveSizeSettling(
                library,
                previous?.SizeBytes,
                previous?.SizeChangedAt?.AsUtc,
                observedSize,
                now);

            var access = settling.IsSettling
                ? (Ok: true, Problem: (string?)null)
                : WatchedFolderScanOps.CheckFileAccess(library.SkipAccessTests, filePath, runtime.OutputFolder);

            if (alreadyCleaned)
            {
                // A cleaned movie still in a library that removes originals: its removal was interrupted. Finish it while the
                // validated output is still there. Never a second pass, and the row keeps its outcome.
                if (!settling.IsSettling && access.Problem is null &&
                    !await WatchedFolderScanOps.ActiveRemuxPassExistsForRelativePathAsync(uow, rel, mediaScope, library.Id).ConfigureAwait(false) &&
                    await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, rel, mediaScope, library.Id, runtime.OutputFolder, filePath).ConfigureAwait(false))
                {
                    await RetryCompletedMovieCleanupAsync(uow, library.Id, runtime.WatchedFolder, filePath, rel, observedSize, settling, now).ConfigureAwait(false);
                }
                else
                {
                    await FileStateStore.TouchLastSeenAsync(uow, previous!.Id, now).ConfigureAwait(false);
                }

                continue;
            }

            var verdict = FileStateDecision.DecideFileState(
                library,
                inWindow,
                FileAgeSeconds(filePath, now),
                pauseReason,
                pauseUntil,
                reopensAt,
                settling.IsSettling,
                settling.Reason,
                settling.StableAt,
                access.Problem,
                outcome.BlockedConnection,
                effectiveMinAgeSeconds,
                now);

            // A pass for this file booked for later is not a file waiting for a free lane; calling it ready would put it
            // first in line on Processing while a lane stood empty. It is on hold until the booked look, for the reason the
            // last pass gave; the pass itself is already queued.
            if (verdict.Eligible &&
                await WatchedFolderScanOps.HeldBackRemuxPassStartsAtAsync(uow, rel, mediaScope, library.Id, now).ConfigureAwait(false) is { } lookAgainAt)
            {
                verdict = new FileStateVerdict(
                    ProcessingFileStatuses.OnHold,
                    previous is { Status: ProcessingFileStatuses.OnHold, StatusReason.Length: > 0 }
                        ? previous.StatusReason
                        : "Weir has another look at this file booked, and leaves it alone until then.",
                    HoldUntil: lookAgainAt);
            }

            await FileStateStore.RecordFileStateAsync(
                uow, library.Id, rel, verdict, observedSize, settling.SizeChangedAt, now).ConfigureAwait(false);
            if (verdict.HoldUntil is { } holdEnds && holdEnds > now && (earliestHoldEnds is null || holdEnds < earliestHoldEnds))
            {
                earliestHoldEnds = holdEnds;
            }

            var cleanupRetryReady = !settling.IsSettling && access.Problem is null;
            var noActivePass = !await WatchedFolderScanOps.ActiveRemuxPassExistsForRelativePathAsync(uow, rel, mediaScope, library.Id).ConfigureAwait(false);
            // Finishing a source removal a lock interrupted: only for a library that removes originals.
            if (mediaScope == ProcessingMediaScopes.Movie && cleanupRetryReady && noActivePass && !keepsOriginals &&
                await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, rel, mediaScope, library.Id, runtime.OutputFolder, filePath).ConfigureAwait(false))
            {
                await RetryCompletedMovieCleanupAsync(uow, library.Id, runtime.WatchedFolder, filePath, rel, observedSize, settling, now).ConfigureAwait(false);
                continue;
            }

            if (!verdict.Eligible)
            {
                continue;
            }

            var facts = new CandidateFileFacts(observedSize, CreatedAtUtc(filePath), ModifiedAtUtc(filePath));
            var rejection = LibraryAdmission.Rejection(rel, Path.GetFileName(filePath), facts, rules);
            if (rejection is not null)
            {
                var reason = rejection.Reason;
                if (rules.RejectedFileAction == "delete_file")
                {
                    pendingRejectedCleanups.Add((rel, filePath, reason, rules.RejectedFileAction));
                    reason = $"{reason} This library is set to delete rejected files; Weir will record this decision before removing only this file.";
                }

                await FileStateStore.RecordFileStateAsync(
                    uow, library.Id, rel, new FileStateVerdict(ProcessingFileStatuses.Skipped, reason), observedSize, settling.SizeChangedAt, now).ConfigureAwait(false);
                continue;
            }

            if (outcome.Verdict != WatchedFileDispatchOutcome.Proceed || !enqueueRemuxJobs)
            {
                continue;
            }

            if (!relThisRun.Add(rel))
            {
                continue;
            }

            if (await WatchedFolderScanOps.ActiveRemuxPassExistsForRelativePathAsync(uow, rel, mediaScope, library.Id).ConfigureAwait(false))
            {
                continue;
            }

            if (!fingerprintDecides &&
                await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, rel, mediaScope, library.Id, runtime.OutputFolder, filePath).ConfigureAwait(false))
            {
                if (mediaScope == ProcessingMediaScopes.Movie && !keepsOriginals)
                {
                    await RetryCompletedMovieCleanupAsync(uow, library.Id, runtime.WatchedFolder, filePath, rel, observedSize, settling, now).ConfigureAwait(false);
                }

                continue;
            }

            // Same cross-connection deadlock hazard as the automatic-retry path above: release uow's
            // write lock before ProcessingJobStore opens its own connection to insert the remux-pass row.
            await uow.CommitAsync().ConfigureAwait(false);
            await EnqueueRemuxPassAsync(uow, context, library, mediaScope, rel, scanTrigger, previous).ConfigureAwait(false);
        }

        // #645: a file that left the watched folder before Weir finished with it stops being listed. Only while the watched
        // folder itself can be read, so an unmounted share never empties the list.
        if (Directory.Exists(runtime.WatchedFolder))
        {
            await ForgetVanishedFilesAsync(uow, library.Id, runtime.WatchedFolder, mediaScope, now).ConfigureAwait(false);
        }

        await uow.CommitAsync().ConfigureAwait(false);

        // A second after the first hold ends, so the look finds it over. A pass booked for later (#646) carries its own
        // start time and needs no look; this is for files the scan itself holds.
        if (earliestHoldEnds is { } firstEnds)
        {
            _wakeups?.Request(library.Id, firstEnds + TimeSpan.FromSeconds(1));
        }

        // The SKIPPED decision above committed before this mutation. If the process stops between the
        // two, the source remains and the next scan safely retries.
        foreach (var (relativePath, filePath, reason, action) in pendingRejectedCleanups)
        {
            var (_, detail) = RemuxPassPaths.CleanupRejectedFile(runtime.WatchedFolder, filePath, action);
            await using var cleanupUow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
            await FileStateStore.MarkFileStatusAsync(cleanupUow, library.Id, relativePath, ProcessingFileStatuses.Skipped, $"{reason} {detail}").ConfigureAwait(false);
            await cleanupUow.CommitAsync().ConfigureAwait(false);
        }
    }

    private static async Task ResetFailureBookkeepingAsync(UnitOfWork uow, ProcessingFileRecord previous)
    {
        await uow.ExecuteAsync(
            "UPDATE files SET failure_class = NULL, failure_attempts = 0, next_retry_at = NULL, status = @status WHERE id = @id",
            ("@status", previous.Status), ("@id", previous.Id)).ConfigureAwait(false);
    }

    /// <summary>How long a file must have been gone, since a scan last saw it, before Weir stops listing it (#645). Longer than a
    /// download client takes to move a file, and than a share takes to come back from a blip.</summary>
    internal static readonly TimeSpan VanishedFileGrace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// #645: a row still waiting, held, failed or cancelled whose file has not been on disk for <see cref="VanishedFileGrace"/>.
    /// The download client removed it, a person deleted it, or the manager took it. The scan walks only files on disk, so nothing
    /// else would ever judge that row again, and it would stay listed for ever. It is forgotten, as Forget does, with one Activity
    /// entry saying why. A file with a pass queued or running is left to that pass, and a path that is now a folder on disk is
    /// kept. Outcomes Weir reached (processed, passed through, rejected, skipped) stay as history.
    /// </summary>
    /// <remarks>
    /// Rows no scan has seen are included, and so are cancelled ones: a media manager's hand-off records its file on receipt,
    /// before any scan sees it, and a cancelled hand-off is one Weir never started. Such a row counts from when it was last
    /// written, so it gets the same grace before it is judged.
    /// </remarks>
    /// <returns>The relative paths of the rows it forgot.</returns>
    internal static async Task<List<string>> ForgetVanishedFilesAsync(UnitOfWork uow, long libraryId, string watchedRoot, string mediaScope, DateTimeOffset now, string trigger = "scan")
    {
        var forgotten = new List<string>();
        var rows = await uow.QueryAsync(
            "SELECT id, relative_path, status, coalesce(last_seen_at, updated_at, created_at) FROM files WHERE library_id = @lib " +
            "AND status IN (@waiting, @held, @outside, @blocked, @failed, @cancelled)",
            reader => (Id: reader.GetInt64(0), RelativePath: reader.GetString(1), Status: reader.GetString(2), LastSeen: PythonTimestamps.Parse(reader.GetValue(3))),
            ("@lib", libraryId),
            ("@waiting", ProcessingFileStatuses.Unprocessed),
            ("@held", ProcessingFileStatuses.OnHold),
            ("@outside", ProcessingFileStatuses.OutOfSchedule),
            ("@blocked", ProcessingFileStatuses.BlockedUpstream),
            ("@failed", ProcessingFileStatuses.ProcessingFailed),
            ("@cancelled", ProcessingFileStatuses.Cancelled)).ConfigureAwait(false);
        var cutoff = now - VanishedFileGrace;
        foreach (var (id, rel, status, lastSeen) in rows)
        {
            if (lastSeen is not { } seen || seen > cutoff)
            {
                continue;
            }

            string path;
            try
            {
                path = Path.GetFullPath(Path.Join(watchedRoot, rel));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(path) || Directory.Exists(path) ||
                await WatchedFolderScanOps.ActiveRemuxPassExistsForRelativePathAsync(uow, rel, mediaScope, libraryId).ConfigureAwait(false))
            {
                continue;
            }

            await FileStateStore.ForgetAsync(uow, id).ConfigureAwait(false);
            await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
                ActivityEventTypes.ProcessingFileLeftWatchedFolder,
                "processing",
                $"{MediaPathNames.Name(rel, OperatingSystem.IsWindows())} left the watched folder before Weir finished with it, so it is no longer listed",
                PyJsonWriter.Dumps(
                    new PyDict()
                        .Set("relative_media_path", rel)
                        .Set("library_id", libraryId)
                        .Set("last_status", status)
                        .Set("trigger", trigger)
                        .Set("result", "skipped"),
                    PyJsonFormat.Compact))).ConfigureAwait(false);
            forgotten.Add(rel);
        }

        return forgotten;
    }

    private static async Task RetryCompletedMovieCleanupAsync(
        UnitOfWork uow, long libraryId, string watchedRoot, string filePath, string rel, long observedSize, SettlingObservation settling, DateTimeOffset now)
    {
        var (cleanupOk, cleanupReason) = WatchedFolderScanOps.RetryCompletedMovieSourceCleanup(watchedRoot, filePath);
        if (cleanupOk)
        {
            var releaseParent = PosixParent(rel);
            var siblings = await uow.QueryAsync(
                "SELECT id, relative_path FROM files WHERE library_id = @lib",
                reader => (Id: reader.GetInt64(0), RelativePath: reader.GetString(1)),
                ("@lib", libraryId)).ConfigureAwait(false);
            foreach (var (id, siblingPath) in siblings)
            {
                if (PosixParent(siblingPath) != releaseParent)
                {
                    continue;
                }

                await uow.ExecuteAsync(
                    "UPDATE files SET status = @status, status_reason = @reason, blocked_by_connection = NULL, hold_until = NULL WHERE id = @id",
                    ("@status", ProcessingFileStatuses.Processed),
                    ("@reason", "Weir removed this file with its release folder after the validated movie output completed. No separate output was created for this extra file."),
                    ("@id", id)).ConfigureAwait(false);
            }

            await FileStateStore.RecordFileStateAsync(
                uow, libraryId, rel,
                new FileStateVerdict(ProcessingFileStatuses.Processed, "The output was already complete. The temporary lock cleared, so Weir finished removing the source release folder."),
                observedSize, settling.SizeChangedAt, now).ConfigureAwait(false);
        }
        else
        {
            // The file is cleaned; only removing its original is waiting. It stays processed, so the fingerprint above keeps
            // recognising it and no scan cleans it again once the manager has imported the output (#644). It is not "on hold":
            // a held file looks unfinished everywhere.
            var retryReason = cleanupReason ?? "The source release folder is still locked.";
            await FileStateStore.RecordFileStateAsync(
                uow, libraryId, rel,
                new FileStateVerdict(ProcessingFileStatuses.Processed, $"The output is complete, but removing the original download is waiting: {retryReason} Weir will try again automatically."),
                observedSize, settling.SizeChangedAt, now).ConfigureAwait(false);
        }
    }

    private async Task EnqueueRemuxPassAsync(
        UnitOfWork uow, JobWorkContext context, ProcessingLibraryRecord library, string mediaScope, string rel, string scanTrigger, ProcessingFileRecord? previousRow)
    {
        var payload = new PyDict()
            .Set("relative_media_path", rel)
            .Set("media_scope", mediaScope)
            .Set("trigger", ActivityProvenance.ScanTriggerToTrigger.GetValueOrDefault(scanTrigger, "manual"))
            .Set("run_id", $"scan-{context.Id.ToString(CultureInfo.InvariantCulture)}")
            .Set("library_id", library.Id);
        // A scan-driven retry (also how a failed hand-off's file is requeued automatically) keeps the hand-off's
        // origin, so a pass-through or reject reached after the retry still has an output path to report and a
        // callback to send, as RequeueStore.RequeueFileAsync does for a manual requeue (#531 item 2).
        if (await HandoffOriginCarry.FindAsync(uow, library.Id, rel).ConfigureAwait(false) is { } origin)
        {
            payload.Set("origin", origin);
        }

        var dedupe = $"{RequeueStore.RemuxPassJobKind}:scan:{Guid.NewGuid():N}";
        var resolutionClass = RunnerUnits.ResolutionClassForDimensions(previousRow?.VideoWidth, previousRow?.VideoHeight);

        // The runner-cost weighting needs the operator settings row already loaded for this job run.
        var operatorSettings = await OperatorSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
        var budget = RunnerBudget.FromSettings(
            operatorSettings.RunnerCapacity, operatorSettings.RunnerCostSd, operatorSettings.RunnerCost720P,
            operatorSettings.RunnerCost1080P, operatorSettings.RunnerCost4K, operatorSettings.RunnerCostUndetermined);

        // The dedupe key above is random, so it cannot stop a second pass for the same file. The file's identity does:
        // look for a pending or leased pass for this file and insert only when there is none, both inside the one
        // BEGIN IMMEDIATE transaction. The caller's earlier ActiveRemuxPassExists check ran on uow, which it had to
        // commit before this (see the deadlock note at the call site), so a hand-off — or another scan — could slip a
        // pass in between the two; this re-check under the write lock closes that window. It also covers the
        // automatic-retry branch, which has no active-pass check of its own.
        var payloadJson = PyJsonWriter.Dumps(payload, PyJsonFormat.Compact);
        var runnerCost = budget.CostFor(resolutionClass);
        await _jobStore.InTransactionAsync(
            (connection, transaction) =>
                WatchedFolderScanOps.ActiveRemuxPassForRelativePath(connection, transaction, rel, mediaScope, library.Id)
                ?? _jobStore.EnqueueOrGet(
                    connection, transaction, dedupe, RequeueStore.RemuxPassJobKind, payloadJson, JobQueueRules.DefaultMaxAttempts, runnerCost, (int)library.Priority)).ConfigureAwait(false);
    }

    private static string PosixParent(string relativePosix)
    {
        var idx = relativePosix.LastIndexOf('/');
        return idx < 0 ? string.Empty : relativePosix[..idx];
    }

    private static long FileSizeBytes(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static double? FileAgeSeconds(string path, DateTimeOffset now)
    {
        try
        {
            var mtime = File.GetLastWriteTimeUtc(path);
            return Math.Max(0.0, (now.UtcDateTime - mtime).TotalSeconds);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTimeOffset? CreatedAtUtc(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The modification time as <c>SourceFiles.Fingerprint</c> measures it (ns since the Unix epoch), or null.</summary>
    private static long? ModifiedTimeNs(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc - DateTime.UnixEpoch).Ticks * 100 : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ModifiedAtUtc(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static PyDict ParsePayload(string? payloadJson)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return new PyDict();
        }

        PyJson data;
        try
        {
            data = PyJsonParser.Parse(raw);
        }
        catch (PyJsonDecodeException exception)
        {
            throw new ArgumentException("watched-folder remux scan dispatch payload must be a JSON object", exception);
        }

        if (data is not PyDict dict)
        {
            throw new ArgumentException("watched-folder remux scan dispatch payload must be a JSON object");
        }

        return dict;
    }
}
