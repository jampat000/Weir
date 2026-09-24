using Microsoft.Extensions.Logging;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// The TV half of the Pass 4 failure-cleanup sweep: a whole season folder is only removed once every direct-child
/// episode is clear (nothing queued or active, and every one has a terminal failed TV remux outcome).
/// </summary>
public sealed partial class ProcessingFailureCleanupSweep
{
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
}
