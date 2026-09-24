using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.IO;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing;

/// <summary>The Movies half of the Pass 4 failure-cleanup sweep: a movie has one source release and one output release.</summary>
public sealed partial class ProcessingFailureCleanupSweep
{
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
}
