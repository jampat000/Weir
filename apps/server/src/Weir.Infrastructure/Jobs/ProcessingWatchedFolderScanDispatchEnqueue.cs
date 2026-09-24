using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Enqueue <c>processing.watched_folder.remux_scan_dispatch.v1</c>: manual HTTP, the periodic scheduler and the
/// folder watcher share these checks and the exact payload/dedupe-key shape.
/// </summary>
public static class ProcessingWatchedFolderScanDispatchEnqueue
{
    private static string ScanJobMediaScope(string? payloadJson)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return ProcessingMediaScopes.Movie;
        }

        WireValue data;
        try
        {
            data = WireJsonParser.Parse(raw);
        }
        catch (WireJsonDecodeException)
        {
            return ProcessingMediaScopes.Movie;
        }

        if (data is not WireObject dict || dict.Get("media_scope") is not WireString scope || scope.Value is not ("movie" or "tv"))
        {
            return ProcessingMediaScopes.Movie;
        }

        return scope.Value;
    }

    private static long? ScanJobLibraryId(string? payloadJson)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        WireValue data;
        try
        {
            data = WireJsonParser.Parse(raw);
        }
        catch (WireJsonDecodeException)
        {
            return null;
        }

        if (data is not WireObject dict || dict.Get("library_id") is not WireInteger value || value.Value <= 0)
        {
            return null;
        }

        return (long)value.Value;
    }

    /// <summary>
    /// True when a pending/leased scan already covers this exact library. A legacy job with no library id still occupies its whole
    /// Movies/TV scope.
    /// </summary>
    public static async Task<bool> QueueHasActiveScanAsync(UnitOfWork uow, string mediaScope, long? libraryId)
    {
        var want = ProcessingMediaScopes.Normalize(mediaScope);
        var rows = await uow.QueryAsync(
            "SELECT payload_json FROM jobs WHERE job_kind = @kind AND status IN ('pending', 'leased')",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            ("@kind", ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch)).ConfigureAwait(false);

        foreach (var payloadJson in rows)
        {
            var jobLibraryId = ScanJobLibraryId(payloadJson);
            if (libraryId is { } wantLibrary)
            {
                if (jobLibraryId == wantLibrary)
                {
                    return true;
                }

                if (jobLibraryId is null && ScanJobMediaScope(payloadJson) == want)
                {
                    return true;
                }

                continue;
            }

            if (ScanJobMediaScope(payloadJson) == want)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shared checks for manual HTTP and periodic enqueue (library found; watched folder saved; output folder saved when live remux is on).
    /// </summary>
    public static async Task<(bool Ok, ScanDispatchPrerequisiteError? Error)> ValidatePrerequisitesAsync(
        UnitOfWork uow, LibraryStore libraries, bool enqueueRemuxJobs, string mediaScope, long? libraryId)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var library = libraryId is { } id ? await libraries.GetAsync(uow, id).ConfigureAwait(false) : null;
        library ??= await libraries.SeededForScopeAsync(uow, mediaScope).ConfigureAwait(false);
        if (library is null)
        {
            // Libraries are the only store (#363): a database with no library covering this scope is one
            // an operator emptied, not an unmigrated one.
            return (false, ScanDispatchPrerequisiteError.NoSavedWatchedFolder);
        }

        var error = ScanDispatchPrerequisites.Validate(library.WatchedFolder, library.OutputFolder, enqueueRemuxJobs);
        return (error is null, error);
    }

    /// <summary>Insert one scan job with a unique dedupe key. Caller commits.</summary>
    public static Task<ProcessingJob> EnqueueScanDispatchJobAsync(
        UnitOfWork uow, ProcessingJobStore jobStore, bool enqueueRemuxJobs, string scanTrigger, string mediaScope, long? libraryId)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        var payload = new WireObject()
            .Set("enqueue_remux_jobs", enqueueRemuxJobs)
            .Set("scan_trigger", ScanDispatchJobPayload.NormalizeTrigger(scanTrigger))
            .Set("media_scope", ProcessingMediaScopes.Normalize(mediaScope));
        if (libraryId is { } id)
        {
            payload.Set("library_id", id);
        }

        var dedupe = $"{ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch}:{Guid.NewGuid():N}";
        return jobStore.EnqueueOrGetAsync(
            dedupe,
            ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch,
            WireJsonWriter.Dumps(payload, WireJsonFormat.Compact));
    }

    /// <summary>
    /// One library's periodic tick. Whether this scope is scheduled at all (#533) is decided by the caller,
    /// <see cref="ProcessingWatchedFolderScanDispatchScheduleTask"/>, before it gets here.
    /// <paramref name="enqueueRemuxJobs"/> is the operator's <c>..._periodic_enqueue_remux_jobs</c> setting.
    /// </summary>
    public static async Task<(bool Inserted, string? Skip)> TryEnqueuePeriodicAsync(
        UnitOfWork uow, ProcessingJobStore jobStore, LibraryStore libraries, ProcessingLibraryRecord library, bool enqueueRemuxJobs)
    {
        ArgumentNullException.ThrowIfNull(library);
        var scope = ProcessingMediaScopes.Normalize(library.MediaType);
        if (await QueueHasActiveScanAsync(uow, scope, library.Id).ConfigureAwait(false))
        {
            return (false, $"active_scan_already_queued_library_{library.Id}");
        }

        var (ok, error) = await ValidatePrerequisitesAsync(uow, libraries, enqueueRemuxJobs, scope, library.Id).ConfigureAwait(false);
        if (!ok)
        {
            return (false, error switch
            {
                ScanDispatchPrerequisiteError.MissingOutputForLiveRemux => "missing_output_for_live_remux",
                _ => "no_saved_watched_folder",
            });
        }

        await EnqueueScanDispatchJobAsync(uow, jobStore, enqueueRemuxJobs, "periodic", scope, library.Id).ConfigureAwait(false);
        return (true, null);
    }

    /// <summary>
    /// Turn a settled burst of filesystem events, or a watcher overflow/error (which asks for the same "look at
    /// this library again" scan), into one job. Identical to what the periodic timer enqueues apart from
    /// <c>scan_trigger</c>, so there is one admission implementation and the watcher is not a second one. Used by
    /// <c>ProcessingWatchedFolderWatcherService</c>.
    /// </summary>
    public static async Task<(bool Inserted, string? Skip)> TryEnqueueForWatcherEventAsync(
        UnitOfWork uow, ProcessingJobStore jobStore, ProcessingLibraryRecord library, bool enqueueRemuxJobs)
    {
        ArgumentNullException.ThrowIfNull(library);
        var scope = ProcessingMediaScopes.Normalize(library.MediaType);
        if (await QueueHasActiveScanAsync(uow, scope, library.Id).ConfigureAwait(false))
        {
            // A queued scan will already look at this file. Adding another would mean two walks of the
            // same tree for one arrival.
            return (false, "active_scan_already_queued");
        }

        if (string.IsNullOrWhiteSpace(library.OutputFolder) && enqueueRemuxJobs)
        {
            return (false, "missing_output_for_live_remux");
        }

        await EnqueueScanDispatchJobAsync(uow, jobStore, enqueueRemuxJobs, "filesystem_event", scope, library.Id).ConfigureAwait(false);
        return (true, null);
    }
}
