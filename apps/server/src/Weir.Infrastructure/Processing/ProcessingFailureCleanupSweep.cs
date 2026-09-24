using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.IO;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// Processing Pass 4: the periodic cleanup sweep for terminal failed remux jobs.
/// Deletes source (and, for Movies, output) folders left behind by a remux that used up its retries, once no media
/// manager still holds the file open. This sweep deletes folders, so an import check it could not make is a stop, not a
/// shrug: every manager covering the scope has to answer before anything is removed.
/// </summary>
/// <remarks>
/// The queue-block check (<c>HeldByManager</c>) uses <see cref="QueueRowMapping"/>'s exact output-path match
/// only; it does not fall back to the title/year anchor the watched-folder scan uses (#522).
/// The movie-specific cleanup lives in the <c>.Movie</c> partial, the TV-specific cleanup (which spans a whole
/// season) in the <c>.Tv</c> partial, and the shared filesystem removal helpers in the <c>.FileOps</c> partial.
/// </remarks>
public sealed partial class ProcessingFailureCleanupSweep
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
}
