using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.IO;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>Activity writes for a Pass 4 failure-cleanup sweep.</summary>
public static class ProcessingFailureCleanupActivity
{
    private static string Label(string mediaScope) => mediaScope == "tv" ? "TV" : "Movies";

    private static PyDict WithOutcome(PyDict detail, string result, string? trigger)
    {
        var copy = detail.Copy();
        if (copy.Get("result") is null)
        {
            copy.Set("result", result);
        }

        if (trigger is not null && copy.Get("trigger") is null)
        {
            copy.Set("trigger", trigger);
        }

        return copy;
    }

    public static Task RecordSweepStartedAsync(UnitOfWork uow, string mediaScope, PyDict detail, string? trigger)
    {
        var withOutcome = WithOutcome(detail, "running", trigger);
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFailureCleanupSweepCompleted, "processing",
            $"Cleanup started for {Label(mediaScope)}",
            PyStrings.Slice(PyJsonWriter.Dumps(withOutcome, PyJsonFormat.Compact), 10_000)));
    }

    public static Task RecordSweepCompletedAsync(UnitOfWork uow, string mediaScope, PyDict detail, string? trigger)
    {
        var label = Label(mediaScope);
        var status = detail.Get("cleanup_run_status") is PyStr statusValue ? statusValue.Value : null;
        var (title, result) = status switch
        {
            "no_eligible_files" => ($"Cleanup checked {label}: no changes needed", "success"),
            "skipped" => ($"Cleanup skipped {label}", "skipped"),
            _ => ($"Cleaned up after failed files ({label})", "success"),
        };
        var withOutcome = WithOutcome(detail, result, trigger);
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFailureCleanupSweepCompleted, "processing", title,
            PyStrings.Slice(PyJsonWriter.Dumps(withOutcome, PyJsonFormat.Compact), 10_000)));
    }
}

/// <summary>
/// Processing Pass 4: the periodic cleanup sweep for terminal failed remux jobs.
/// Deletes source (and, for Movies, output) folders left behind by a remux that used up its retries, once no media
/// manager still holds the file open. This sweep deletes folders, so an import check it could not make is a stop, not a
/// shrug: every manager covering the scope has to answer before anything is removed.
/// </summary>
/// <remarks>
/// The queue-block check (<c>HeldByManager</c>) uses <see cref="QueueRowMapping"/>'s exact output-path match
/// only; it does not fall back to the title/year anchor the watched-folder scan uses (#522).
/// </remarks>
public sealed class ProcessingFailureCleanupSweep
{
    /// <summary>A failed job whose recorded path resolves outside the watched folder is never acted on.</summary>
    public const string OutsideWatchedFolderReason = "The failed file's recorded path is outside the watched folder, so nothing was removed.";

    private readonly SqliteDatabase _database;
    private readonly WeirOptions _options;
    private readonly MediaManagerConnectionService _connections;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessingFailureCleanupSweep> _logger;

    public ProcessingFailureCleanupSweep(SqliteDatabase database, WeirOptions options, MediaManagerConnectionService connections, TimeProvider time, ILogger<ProcessingFailureCleanupSweep> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Exposed so the job handler can write its started/completed Activity rows in their own short
    /// transactions, rather than holding one open across the sweep's filesystem work (see the class remarks in
    /// <c>RemuxPassHandler</c> for why this codebase never holds a write transaction across slow I/O).</summary>
    public SqliteDatabase Database => _database;

    public Microsoft.Extensions.Logging.ILogger Logger => _logger;

    public async Task<PyDict> RunForScopeAsync(string mediaScope, CancellationToken cancellationToken)
    {
        var scope = mediaScope == "tv" ? "tv" : "movie";
        var now = _time.GetUtcNow();
        var grace = TimeSpan.FromSeconds(Math.Max(0, scope == "tv" ? _options.ProcessingTvFailureCleanupGracePeriodSeconds : _options.ProcessingMovieFailureCleanupGracePeriodSeconds));
        var olderThan = now - grace;

        return await LockedWrites.RunAsync(
            _database,
            uow => RunForScopeAsync(uow, scope, olderThan, cancellationToken),
            _logger,
            "failure cleanup sweep",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PyDict> RunForScopeAsync(UnitOfWork uow, string scope, DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var library = await RemuxPassHandler.ResolveLibraryAsync(uow, null, scope).ConfigureAwait(false);
        string watchedRaw = string.Empty, outputRaw = string.Empty, workRaw = string.Empty;
        if (library is not null)
        {
            watchedRaw = library.WatchedFolder;
            outputRaw = library.OutputFolder;
            var folderRow = new ProcessingLibraryFolderRow(library.Id, library.MediaType, 0, library.WorkFolder, library.OutputFolder);
            workRaw = ProcessingLibraryFolders.EffectiveWorkFolder(folderRow, _options.WeirHome);
        }

        var workRoot = RemuxPassPaths.Resolve(workRaw.Length == 0 ? "." : workRaw);
        var outResult = new PyDict()
            .Set("media_scope", scope)
            .Set("cleanup_run_status", "started")
            .Set("grace_period_seconds", (long)Math.Max(0, scope == "tv" ? _options.ProcessingTvFailureCleanupGracePeriodSeconds : _options.ProcessingMovieFailureCleanupGracePeriodSeconds))
            .Set("eligible_failed_jobs", 0L)
            .Set("processed_failed_jobs", 0L)
            .Set("skip_reason", (string?)null)
            .Set("jobs", new PyList());

        if (PyStrings.Strip(watchedRaw).Length == 0 || PyStrings.Strip(outputRaw).Length == 0)
        {
            outResult.Set("cleanup_run_status", "skipped");
            outResult.Set("skip_reason", "Saved watched/output paths are not configured for this scope, so failure cleanup was skipped safely.");
            return outResult;
        }

        var watchedRoot = RemuxPassPaths.Resolve(watchedRaw);
        var outputRoot = RemuxPassPaths.Resolve(outputRaw);
        var failedRows = await FailedJobsForScopeAsync(uow, scope, olderThan).ConfigureAwait(false);
        outResult.Set("eligible_failed_jobs", (long)failedRows.Count);
        if (failedRows.Count == 0)
        {
            outResult.Set("cleanup_run_status", "no_eligible_files");
            outResult.Set("skip_reason", "No eligible failed jobs were old enough for cleanup.");
            return outResult;
        }

        var signals = await _connections.CollectQueueSignalsAsync(uow, scope, cancellationToken: cancellationToken).ConfigureAwait(false);
        var report = ReportForSignals(signals);
        string? queueUnreachable = null;
        if (report.Consulted == 0)
        {
            var scopeWord = scope == "tv" ? "TV episodes" : "Movies";
            queueUnreachable = $"No media manager is connected for {scopeWord}.";
        }
        else if (!report.AllReported)
        {
            queueUnreachable = report.SilentDetails[0];
        }

        var jobsList = new PyList();
        var processed = 0L;
        foreach (var (jobId, relNorm, legacyDryRun) in failedRows)
        {
            var detail = new PyDict()
                .Set("job_id", jobId)
                .Set("relative_media_path", relNorm)
                .Set($"{scope}_failure_cleanup_ran", false)
                .Set($"{scope}_failure_cleanup_skip_reason", (string?)null)
                .Set($"{scope}_failure_cleanup_dry_run", legacyDryRun)
                .Set($"{scope}_failure_cleanup_queue_check", "skipped")
                .Set($"{scope}_failure_cleanup_temp_files_deleted", new PyList())
                .Set($"{scope}_failure_cleanup_cascade_folders_deleted", new PyList());
            processed++;
            jobsList.Items.Add(detail);

            if (legacyDryRun)
            {
                detail.Set(
                    $"{scope}_failure_cleanup_skip_reason",
                    "Skipped for compatibility: this failed remux row uses legacy dry_run payload format, which is not a current mode.");
                continue;
            }

            if (queueUnreachable is not null)
            {
                detail.Set($"{scope}_failure_cleanup_skip_reason", $"Weir could not check whether anything is still importing, so nothing was removed. {queueUnreachable}");
                continue;
            }

            // Path.Join, not Path.Combine: a rooted path in a job payload must not replace the watched folder.
            var srcFile = RemuxPassPaths.Resolve(Path.Join(watchedRoot, relNorm));
            if (!PathContainment.IsUnder(watchedRoot, srcFile))
            {
                detail.Set($"{scope}_failure_cleanup_skip_reason", OutsideWatchedFolderReason);
                continue;
            }

            var srcFolder = Path.GetDirectoryName(srcFile) ?? watchedRoot;

            if (scope == "movie")
            {
                ProcessMovie(detail, srcFile, relNorm, watchedRoot, outputRoot, workRoot, library?.MediaExtensionsCsv, signals);
            }
            else
            {
                await ProcessTvAsync(uow, detail, srcFolder, relNorm, watchedRoot, outputRoot, workRoot, signals).ConfigureAwait(false);
            }
        }

        outResult.Set("processed_failed_jobs", processed);
        outResult.Set("jobs", jobsList);
        outResult.Set("cleanup_run_status", "completed");
        return outResult;
    }

    private void ProcessMovie(
        PyDict detail, string srcFile, string relNorm, string watchedRoot, string outputRoot, string workRoot, string? mediaExtensionsCsv,
        IReadOnlyList<ManagerQueueSignal> signals)
    {
        var srcFolder = Path.GetDirectoryName(srcFile) ?? watchedRoot;
        detail.Set("movie_failure_cleanup_source_folder_deleted", false);
        detail.Set("movie_failure_cleanup_source_folder_path", srcFolder);
        detail.Set("movie_failure_cleanup_output_folder_deleted", false);
        var outFile = RemuxPassPaths.Resolve(Path.Join(outputRoot, relNorm));
        var outFolder = Path.GetDirectoryName(outFile) ?? outputRoot;
        detail.Set("movie_failure_cleanup_output_folder_path", outFolder);

        var holder = HeldByManager(signals, "movie", srcFile);
        if (holder is not null)
        {
            detail.Set("movie_failure_cleanup_queue_check", "blocked_in_queue");
            detail.Set("movie_failure_cleanup_skip_reason", $"{holder} is still importing this file, so failure cleanup skipped.");
            return;
        }

        detail.Set("movie_failure_cleanup_queue_check", "passed_not_in_queue");
        detail.Set("movie_failure_cleanup_ran", true);

        var cascade = (PyList)detail.Get("movie_failure_cleanup_cascade_folders_deleted")!;
        if (PathContainment.IsUnder(watchedRoot, srcFolder) && Directory.Exists(srcFolder))
        {
            var removal = RemoveRelease(watchedRoot, srcFile, mediaExtensionsCsv);
            detail.Set("movie_failure_cleanup_source_folder_deleted", removal.FolderRemoved);
            if (removal.FolderRemoved)
            {
                CascadeUnderRoot(Path.GetDirectoryName(srcFolder) ?? watchedRoot, watchedRoot, cascade);
            }
            else if (removal.Reason is { } kept)
            {
                detail.Set("movie_failure_cleanup_source_folder_kept_reason", kept);
            }
        }

        if (PathContainment.IsUnder(outputRoot, outFolder) && Directory.Exists(outFolder))
        {
            var removal = RemoveRelease(outputRoot, outFile, mediaExtensionsCsv);
            detail.Set("movie_failure_cleanup_output_folder_deleted", removal.FolderRemoved);
            if (removal.FolderRemoved)
            {
                CascadeUnderRoot(Path.GetDirectoryName(outFolder) ?? outputRoot, outputRoot, cascade);
            }
            else if (removal.Reason is { } kept)
            {
                detail.Set("movie_failure_cleanup_output_folder_kept_reason", kept);
            }
        }

        var tempDeleted = (PyList)detail.Get("movie_failure_cleanup_temp_files_deleted")!;
        foreach (var temp in JobTempCandidates(workRoot, relNorm))
        {
            var (ok, _) = SafeUnlink(temp);
            if (ok)
            {
                tempDeleted.Items.Add(new PyStr(temp));
            }
        }
    }

    private async Task ProcessTvAsync(
        UnitOfWork uow, PyDict detail, string srcSeason, string relNorm, string watchedRoot, string outputRoot, string workRoot,
        IReadOnlyList<ManagerQueueSignal> signals)
    {
        detail.Set("tv_failure_cleanup_season_folder_deleted", false);
        detail.Set("tv_failure_cleanup_output_season_deleted", false);
        detail.Set("tv_failure_cleanup_season_folder_path", srcSeason);
        var relDirectory = Path.GetDirectoryName(relNorm.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var outSeason = RemuxPassPaths.Resolve(relDirectory.Length == 0 ? outputRoot : Path.Join(outputRoot, relDirectory));
        detail.Set("tv_failure_cleanup_output_season_path", outSeason);

        var episodes = new List<string>();
        if (Directory.Exists(srcSeason))
        {
            try
            {
                episodes = [.. Directory.EnumerateFiles(srcSeason)
                    .Where(IntakeRules.IsMediaCandidateName)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                episodes = [];
            }
        }

        if (episodes.Count == 0)
        {
            detail.Set("tv_failure_cleanup_skip_reason", "No direct-child episode media files were found in this season folder, so season cleanup was skipped.");
            return;
        }

        var blocked = false;
        foreach (var episode in episodes)
        {
            if (HeldByManager(signals, "tv", episode) is not null)
            {
                blocked = true;
                break;
            }

            var rel = RemuxPassPaths.RelativeTo(episode, watchedRoot);
            if (rel is null)
            {
                _logger.LogWarning("TV failure cleanup skipped episode outside watched root path={Path}", episode);
                continue;
            }

            var relPosix = RemuxPassPaths.Posix(rel);
            if (await ActiveRemuxPassExistsAsync(uow, relPosix, "tv").ConfigureAwait(false))
            {
                blocked = true;
                break;
            }

            if (!await TvHasTerminalFailedRemuxAsync(uow, relPosix).ConfigureAwait(false))
            {
                blocked = true;
                break;
            }
        }

        if (blocked)
        {
            detail.Set("tv_failure_cleanup_queue_check", "blocked_in_queue_or_active_job");
            detail.Set(
                "tv_failure_cleanup_skip_reason",
                "TV season is not clear yet (episode still queued, active TV remux exists, or not every direct-child episode has a terminal failed TV remux outcome), so cleanup skipped.");
            return;
        }

        detail.Set("tv_failure_cleanup_queue_check", "passed_not_in_queue");
        detail.Set("tv_failure_cleanup_ran", true);

        if (RemuxPassPaths.RelativeTo(srcSeason, watchedRoot) is not null && !RemuxPassPaths.SamePath(srcSeason, watchedRoot) && Directory.Exists(srcSeason))
        {
            var (ok, _) = SafeRmTree(watchedRoot, srcSeason);
            detail.Set("tv_failure_cleanup_season_folder_deleted", ok);
            if (ok)
            {
                CascadeUnderRoot(Path.GetDirectoryName(srcSeason) ?? watchedRoot, watchedRoot, (PyList)detail.Get("tv_failure_cleanup_cascade_folders_deleted")!);
            }
        }

        if (RemuxPassPaths.RelativeTo(outSeason, outputRoot) is not null && !RemuxPassPaths.SamePath(outSeason, outputRoot) && Directory.Exists(outSeason))
        {
            var (ok, _) = SafeRmTree(outputRoot, outSeason);
            detail.Set("tv_failure_cleanup_output_season_deleted", ok);
            if (ok)
            {
                CascadeUnderRoot(Path.GetDirectoryName(outSeason) ?? outputRoot, outputRoot, (PyList)detail.Get("tv_failure_cleanup_cascade_folders_deleted")!);
            }
        }

        var tempDeleted = (PyList)detail.Get("tv_failure_cleanup_temp_files_deleted")!;
        foreach (var temp in JobTempCandidates(workRoot, relNorm))
        {
            var (ok, _) = SafeUnlink(temp);
            if (ok)
            {
                tempDeleted.Items.Add(new PyStr(temp));
            }
        }
    }

    /// <summary>The connection whose queue still names this exact file, or null. Path equality only.</summary>
    private static string? HeldByManager(IReadOnlyList<ManagerQueueSignal> signals, string mediaScope, string mediaFile)
    {
        var dialect = QueueRowMapping.DialectForScope(mediaScope);
        var candidatePath = RemuxPassPaths.Resolve(mediaFile);
        foreach (var signal in signals)
        {
            if (!signal.IsReported)
            {
                continue;
            }

            foreach (var row in signal.Rows)
            {
                if (row.Scope != mediaScope)
                {
                    continue;
                }

                if (QueueRowMapping.MapQueueRowToProcessingView(row.Payload, dialect, candidatePath).AppliesToFile)
                {
                    return signal.Connection.Label;
                }
            }
        }

        return null;
    }

    private sealed record SignalReport(int Consulted, bool AllReported, IReadOnlyList<string> SilentDetails);

    /// <summary>How many managers were consulted, whether all of them answered, and what the silent ones said.</summary>
    private static SignalReport ReportForSignals(IReadOnlyList<ManagerQueueSignal> signals)
    {
        var silentDetails = new List<string>();
        foreach (var signal in signals)
        {
            if (signal.IsReported)
            {
                continue;
            }

            silentDetails.Add(signal.Detail ?? $"{signal.Connection.Label} did not answer.");
        }

        return new SignalReport(signals.Count, silentDetails.Count == 0, silentDetails);
    }

    private async Task<List<(long JobId, string Rel, bool LegacyDryRun)>> FailedJobsForScopeAsync(UnitOfWork uow, string mediaScope, DateTimeOffset olderThan)
    {
        var rows = await uow.QueryAsync(
            "SELECT id, payload_json, updated_at FROM jobs WHERE job_kind = $kind AND status = $status",
            reader => (Id: reader.GetInt64(0), Payload: reader.IsDBNull(1) ? null : reader.GetString(1), UpdatedAt: reader.IsDBNull(2) ? null : reader.GetValue(2)),
            ("$kind", RemuxPassOutcomes.JobKind),
            ("$status", ProcessingJobStatus.Failed)).ConfigureAwait(false);

        var result = new List<(long, string, bool)>();
        foreach (var row in rows)
        {
            var updatedAt = PythonTimestamps.Parse(row.UpdatedAt) ?? DateTimeOffset.MinValue;
            if (updatedAt >= olderThan)
            {
                continue;
            }

            (string Rel, string Scope, bool LegacyDryRun)? parsed;
            try
            {
                parsed = ParseFailedJobPayload(row.Payload);
            }
            catch (Exception exception) when (exception is FormatException or PyJsonDecodeException)
            {
                _logger.LogDebug(exception, "Failure cleanup ignored malformed failed-job payload job_id={JobId}", row.Id);
                continue;
            }

            if (parsed.Value.Scope != mediaScope)
            {
                continue;
            }

            result.Add((row.Id, parsed.Value.Rel, parsed.Value.LegacyDryRun));
        }

        return result;
    }

    private static async Task<bool> TvHasTerminalFailedRemuxAsync(UnitOfWork uow, string relativePosix)
    {
        var want = NormRel(relativePosix);
        var rows = await uow.QueryAsync(
            "SELECT payload_json FROM jobs WHERE job_kind = $kind AND status = $status",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            ("$kind", RemuxPassOutcomes.JobKind),
            ("$status", ProcessingJobStatus.Failed)).ConfigureAwait(false);
        foreach (var payload in rows)
        {
            (string Rel, string Scope, bool LegacyDryRun)? parsed;
            try
            {
                parsed = ParseFailedJobPayload(payload);
            }
            catch (Exception exception) when (exception is FormatException or PyJsonDecodeException)
            {
                continue;
            }

            if (parsed.Value.Scope == "tv" && parsed.Value.Rel == want)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> ActiveRemuxPassExistsAsync(UnitOfWork uow, string relativePosix, string mediaScope)
    {
        var wantScope = mediaScope is "movie" or "tv" ? mediaScope : "movie";
        var rows = await uow.QueryAsync(
            "SELECT payload_json FROM jobs WHERE job_kind = $kind AND status IN ($pending, $leased)",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            ("$kind", RemuxPassOutcomes.JobKind),
            ("$pending", ProcessingJobStatus.Pending),
            ("$leased", ProcessingJobStatus.Leased)).ConfigureAwait(false);
        foreach (var payload in rows)
        {
            var raw = PyStrings.Strip(payload ?? string.Empty);
            if (raw.Length == 0)
            {
                continue;
            }

            PyJson parsedJson;
            try
            {
                parsedJson = PyJsonParser.Parse(raw);
            }
            catch (PyJsonDecodeException)
            {
                continue;
            }

            if (parsedJson is not PyDict data)
            {
                continue;
            }

            var rel = data.Get("relative_media_path") is PyStr relStr ? relStr.Value : null;
            var jobScope = data.Get("media_scope") is PyStr scopeStr && scopeStr.Value is "movie" or "tv" ? scopeStr.Value : "movie";
            if (rel is not null && PyStrings.Strip(rel) == relativePosix && jobScope == wantScope)
            {
                return true;
            }
        }

        return false;
    }

    private static (string Rel, string Scope, bool LegacyDryRun) ParseFailedJobPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new FormatException("missing payload_json");
        }

        if (PyJsonParser.Parse(payloadJson) is not PyDict data)
        {
            throw new FormatException("payload_json must be object");
        }

        if (data.Get("relative_media_path") is not PyStr relStr || PyStrings.Strip(relStr.Value).Length == 0)
        {
            throw new FormatException("payload missing relative_media_path");
        }

        var scope = data.Get("media_scope") is PyStr scopeStr && string.Equals(PyStrings.Strip(scopeStr.Value), "tv", StringComparison.OrdinalIgnoreCase)
            ? "tv"
            : "movie";
        var legacyDryRun = data.Get("dry_run") is { IsTruthy: true };
        return (NormRel(relStr.Value), scope, legacyDryRun);
    }

    /// <summary>A relative path with forward slashes and no empty or <c>.</c> segments.</summary>
    private static string NormRel(string raw)
    {
        var trimmed = PyStrings.Strip(raw).Replace('\\', '/');
        var parts = trimmed.Split('/').Where(part => part.Length > 0 && part != ".").ToArray();
        return parts.Length == 0 ? string.Empty : string.Join('/', parts);
    }

    /// <summary>The work-folder temp files Weir created for this source, matched by <see cref="WeirTempFiles.RemuxTempNameFor"/>.</summary>
    private static List<string> JobTempCandidates(string workRoot, string relNorm)
    {
        var result = new List<string>();
        if (relNorm.Length == 0 || !Directory.Exists(workRoot))
        {
            return result;
        }

        // Only the exact temp names Weir creates for this source (#534); an operator's own "Film.processing.notes.txt"
        // beside a failed "Film.mkv" is not Weir's to delete.
        var ownTempName = WeirTempFiles.RemuxTempNameFor(relNorm);
        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(workRoot)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var child in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (ownTempName.IsMatch(Path.GetFileName(child)))
            {
                result.Add(child);
            }
        }

        return result;
    }

    /// <summary>Removes now-empty ancestor folders, up to (not including) the root.</summary>
    private static void CascadeUnderRoot(string firstParent, string root, PyList deletedOut)
    {
        var current = RemuxPassPaths.Resolve(firstParent);
        var rr = RemuxPassPaths.Resolve(root);
        while (!RemuxPassPaths.SamePath(current, rr))
        {
            if (RemuxPassPaths.RelativeTo(current, rr) is null || !Directory.Exists(current))
            {
                break;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                break;
            }

            try
            {
                Directory.Delete(current);
                deletedOut.Items.Add(new PyStr(current));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }
    }

    /// <summary>A failed movie's source or output release, removed by <see cref="ReleaseFolderRemoval"/>; a locked folder is logged, not thrown.</summary>
    private ReleaseRemoval RemoveRelease(string root, string file, string? mediaExtensionsCsv)
    {
        try
        {
            return ReleaseFolderRemoval.Remove(root, file, mediaExtensionsCsv);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Failure cleanup could not remove the release of {File}; it is in use or blocked.", file);
            return new ReleaseRemoval(ReleaseRemovalKind.NothingRemoved, null);
        }
    }

    private (bool Ok, string? Error) SafeRmTree(string root, string path)
    {
        if (PathContainment.HasLinkBelowRoot(root, path))
        {
            _logger.LogWarning("Failure cleanup left {Path} alone: it, or a folder above it, is a link to another place.", path);
            return (false, ReleaseFolderRemoval.LinkedFolderReason);
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var message = $"Could not remove {path} because it is in use or blocked ({exception.Message}).";
            _logger.LogWarning("Failure cleanup: {Message}", message);
            return (false, message);
        }
    }

    private (bool Ok, string? Error) SafeUnlink(string path)
    {
        try
        {
            File.Delete(path);
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var message = $"Could not remove temp file {path} because it is in use or blocked ({exception.Message}).";
            _logger.LogWarning("Failure cleanup: {Message}", message);
            return (false, message);
        }
    }
}
