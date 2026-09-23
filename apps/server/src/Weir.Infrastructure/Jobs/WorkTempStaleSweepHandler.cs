using System.Globalization;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Handles <c>processing.work_temp_stale_sweep.v1</c>: removes Weir's own remux temp output that has been left in a
/// work folder. The job was queued every hour but nothing handled it, so it waited in the queue for ever and the files
/// it was for were only ever cleared when Weir restarted.
/// </summary>
/// <remarks>
/// It removes exactly what start-up recovery removes (<see cref="WeirTempFiles.IsRemuxTempName"/>, only at the top of
/// a work folder: the scope's default one and every library's effective one) with two differences that make it safe
/// while Weir is running. A file must be untouched for the configured stale age (a day by default), and a file that
/// belongs to a pass running right now is never touched, however old.
/// </remarks>
public sealed class WorkTempStaleSweepHandler : IJobHandler
{
    private readonly ProcessingJobStore _store;
    private readonly WeirOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<WorkTempStaleSweepHandler> _logger;

    public WorkTempStaleSweepHandler(ProcessingJobStore store, WeirOptions options, TimeProvider time, ILogger<WorkTempStaleSweepHandler> logger)
    {
        _store = store;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public string JobKind => PeriodicJobKinds.WorkTempStaleSweep;

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var payload = JobPayload.ParseObject(context.PayloadJson);
        var scope = ProcessingLibraryFolders.NormalizeMediaScope(JobPayload.StringProperty(payload, "media_scope"));
        var trigger = JobPayload.StringProperty(payload, "trigger");

        var (libraries, running) = await _store.InTransactionAsync(
            (connection, transaction) => (
                ProcessingLibraryFolders.List(connection, transaction),
                ProcessingJobStore.Query(
                    connection,
                    transaction,
                    "SELECT id, dedupe_key, job_kind, payload_json, status, lease_owner, lease_expires_at, attempt_count, max_attempts, " +
                    "last_error, not_before, runner_cost, priority, created_at, updated_at FROM jobs WHERE status = @leased",
                    ("@leased", ProcessingJobStatus.Leased))),
            cancellationToken).ConfigureAwait(false);

        var roots = WorkRoots(libraries, scope, _options.WeirHome);
        var protectedNames = running
            .Select(job => JobPayload.StringProperty(JobPayload.ParseObject(job.PayloadJson), "relative_media_path")?.Trim())
            .Where(relative => !string.IsNullOrEmpty(relative))
            .Select(relative => WeirTempFiles.RemuxTempNameFor(relative!))
            .ToList();
        var cutoff = _time.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(_options.ProcessingWorkTempStaleSweepMinStaleAgeSeconds);

        var removed = 0;
        foreach (var root in roots)
        {
            removed += Sweep(root, cutoff, protectedNames);
        }

        var label = scope == "tv" ? "TV" : "Movies";
        var detail = new PyDict()
            .Set("job_id", context.Id)
            .Set("media_scope", scope)
            .Set("files_removed", removed)
            .Set("min_stale_age_seconds", _options.ProcessingWorkTempStaleSweepMinStaleAgeSeconds)
            .Set("result", "success");
        if (trigger is not null)
        {
            detail.Set("trigger", trigger);
        }

        var title = removed == 0
            ? $"Work files checked ({label}): nothing left behind"
            : $"Cleared {removed.ToString(CultureInfo.InvariantCulture)} left-behind work {(removed == 1 ? "file" : "files")} ({label})";
        await LockedWrites.RunAsync(
            _store.Database,
            uow => SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
                ActivityEventTypes.ProcessingWorkTempStaleSweepCompleted, "processing", title, PyJsonWriter.Dumps(detail, PyJsonFormat.Compact))),
            _logger,
            "work file sweep completed",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The scope's default work folder and the effective work folder of every library of that scope.</summary>
    private static HashSet<string> WorkRoots(IReadOnlyList<ProcessingLibraryFolderRow> libraries, string scope, string weirHome)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = new HashSet<string>(comparer)
        {
            scope == "tv" ? ProcessingLibraryFolders.DefaultTvWorkFolder(weirHome) : ProcessingLibraryFolders.DefaultMovieWorkFolder(weirHome),
        };
        foreach (var library in libraries.Where(l => ProcessingLibraryFolders.NormalizeMediaScope(l.MediaType) == scope))
        {
            roots.Add(ProcessingLibraryFolders.EffectiveWorkFolder(library, weirHome));
        }

        return roots;
    }

    private int Sweep(string folder, DateTime cutoffUtc, IReadOnlyList<System.Text.RegularExpressions.Regex> protectedNames)
    {
        string root;
        try
        {
            root = ProcessingLibraryFolders.ExpandForFilesystem(folder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return 0;
        }

        if (!Directory.Exists(root))
        {
            return 0;
        }

        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0 })];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The work file sweep could not read {Folder}: {Error}", root, exception.Message);
            return 0;
        }

        var removed = 0;
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (!WeirTempFiles.IsRemuxTempName(name) || protectedNames.Any(pattern => pattern.IsMatch(name)))
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(path) > cutoffUtc)
                {
                    continue;
                }

                File.Delete(path);
                removed++;
                _logger.LogInformation("The work file sweep removed left-behind temp output {Path}", path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("The work file sweep could not remove {Path}: {Error}", path, exception.Message);
            }
        }

        return removed;
    }
}
