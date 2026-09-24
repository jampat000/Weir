using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Rules;
using Weir.Infrastructure.Media;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassRunner
{
    private async Task<PyDict> RemuxAndPublishAsync(
        PassContext context,
        PyDict output,
        RemuxPlan plan,
        IReadOnlyList<string> argv,
        AccelerationDecision hardware,
        string audioBefore,
        string audioAfter,
        string subsBefore,
        string subsAfter,
        string relative,
        string collisionPolicy,
        IReadOnlyList<string> sidecarPatterns,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var report = request.ProgressReporter;
        var relativeMediaPath = context.RelativeMediaPath;
        var src = context.Source;
        var workDir = request.Runtime.WorkFolderEffective;
        string final;
        CollisionDecision collision;
        bool replacedExisting;
        try
        {
            var workDisk = FileLifecycle.CheckMinimumFreeDiskSpace(Path.Join(workDir, $".{Path.GetFileName(src)}.work-preflight"), request.MinimumFreeDiskSpaceMb, FreeBytes);
            if (!workDisk.Ok)
            {
                return SkipGuardrail(
                    relativeMediaPath,
                    workDisk.Message,
                    "minimum_free_disk_space",
                    context.Inspected,
                    new PyDict()
                        .Set("disk_checked_path", workDisk.CheckedPath)
                        .Set("disk_free_mb", Math.Round(workDisk.FreeMb, 1, MidpointRounding.ToEven))
                        .Set("minimum_free_disk_space_mb", workDisk.RequiredMb)
                        .Set("media_scope", context.Scope)
                        .Set("stream_counts", output["stream_counts"])
                        .Set("plan_summary", output["plan_summary"])
                        .Set("audio_before", audioBefore)
                        .Set("audio_after", audioAfter)
                        .Set("subs_before", subsBefore)
                        .Set("subs_after", subsAfter)
                        .Set("remux_required", true));
            }

            report?.Invoke(new PyDict()
                .Set("status", "processing")
                .Set("percent", 0.0)
                .Set("eta_seconds", PyNull.Instance)
                .Set("elapsed_seconds", 0L)
                .Set("relative_media_path", relativeMediaPath)
                .Set("inspected_source_path", context.Inspected)
                .Set("media_scope", context.Scope)
                .Set("stream_counts", output["stream_counts"])
                .Set("duration_seconds", NullableFloat(context.Duration))
                // What comes out, so Live can say it while the file is being written, not only afterwards.
                .Set("removed_audio", output["removed_audio"])
                .Set("removed_subtitles", output["removed_subtitles"])
                .Set("message", "Weir has started writing the cleaned-up file."));
            // #548: the library's writer choice. The staged output keeps the source's own extension
            // (RemuxToTempFileAsync), so the source path is what decides whether mkvmerge can take it.
            var writer = new RemuxWriterSelector(
                new FfmpegRemuxWriter(_tools),
                new MkvmergeRemuxWriter(_tools, _resolver)).Select(src, request.Runtime.RemuxWriter);
            var tmp = await _tools.RemuxToTempFileAsync(
                src,
                workDir,
                plan,
                context.SourceProbe,
                context.SourceWarnings,
                report is null
                    ? null
                    : update => report(ProgressWithUpdate(
                        new PyDict()
                            .Set("status", "processing")
                            .Set("relative_media_path", relativeMediaPath)
                            .Set("inspected_source_path", context.Inspected)
                            .Set("media_scope", context.Scope)
                            .Set("stream_counts", output["stream_counts"])
                            .Set("duration_seconds", NullableFloat(context.Duration))
                            .Set("removed_audio", output["removed_audio"])
                            .Set("removed_subtitles", output["removed_subtitles"])
                            .Set("message", "Weir is writing the cleaned-up file."),
                        update)),
                context.Duration,
                hardware,
                writer,
                request.Runtime.RewriteWithFfmpeg,
                request.KeepFailedWorkFiles,
                cancellationToken).ConfigureAwait(false);
            try
            {
                AssertSourceUnchanged(src, context.Expected);
            }
            catch (MediaCompletenessException)
            {
                FileLifecycle.BestEffortDelete(tmp);
                throw;
            }

            final = Path.Join(context.OutputDirectory, relative);
            var outputDisk = FileLifecycle.CheckMinimumFreeDiskSpace(final, request.MinimumFreeDiskSpaceMb, FreeBytes);
            if (!outputDisk.Ok)
            {
                FileLifecycle.BestEffortDelete(tmp);
                return SkipGuardrail(
                    relativeMediaPath,
                    outputDisk.Message,
                    "minimum_free_disk_space",
                    context.Inspected,
                    new PyDict()
                        .Set("disk_checked_path", outputDisk.CheckedPath)
                        .Set("disk_free_mb", Math.Round(outputDisk.FreeMb, 1, MidpointRounding.ToEven))
                        .Set("minimum_free_disk_space_mb", outputDisk.RequiredMb)
                        .Set("media_scope", context.Scope)
                        .Set("processing_output_folder_resolved", context.OutputDirectory)
                        .Set("stream_counts", output["stream_counts"])
                        .Set("plan_summary", output["plan_summary"])
                        .Set("audio_before", audioBefore)
                        .Set("audio_after", audioAfter)
                        .Set("subs_before", subsBefore)
                        .Set("subs_after", subsAfter)
                        .Set("remux_required", true));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            // The collision policy and the decision are both recorded, so a collision is never silent (#349).
            collision = OutputCollision.Decide(final, src, tmp, collisionPolicy);
            replacedExisting = collision.ReplacedExisting;
            if (collision.Wrote)
            {
                final = collision.Destination;
                Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                FileLifecycle.SafeFinalizeFile(tmp, final, _ownership);
            }
        }
        catch (MediaCompletenessException exception)
        {
            report?.Invoke(new PyDict()
                .Set("status", "waiting")
                .Set("percent", PyNull.Instance)
                .Set("eta_seconds", PyNull.Instance)
                .Set("relative_media_path", relativeMediaPath)
                .Set("inspected_source_path", context.Inspected)
                .Set("media_scope", context.Scope)
                .Set("message", "Weir is waiting for this file to finish downloading.")
                .Set("reason", exception.Message));
            return SourceNotReady(relativeMediaPath, exception.Message, context.Inspected);
        }
#pragma warning disable CA1031 // Any failure while writing becomes this file's recorded failure; the worker survives it.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            report?.Invoke(new PyDict()
                .Set("status", "failed")
                .Set("percent", PyNull.Instance)
                .Set("eta_seconds", PyNull.Instance)
                .Set("relative_media_path", relativeMediaPath)
                .Set("inspected_source_path", context.Inspected)
                .Set("media_scope", context.Scope)
                .Set("message", "Weir could not finish this file.")
                .Set("reason", exception.Message));
            return new PyDict()
                .Set("ok", false)
                .Set("outcome", RemuxPassOutcomes.FailedDuringExecution)
                .Set("preflight_status", "ok")
                .Set("preflight_reason", "ffprobe completed and remux plan was evaluated")
                .Set("reason", exception.Message)
                .Set("relative_media_path", relativeMediaPath)
                .Set("inspected_source_path", context.Inspected)
                .Set("processing_watched_folder_resolved", context.WatchedRoot)
                .Set("stream_counts", output["stream_counts"])
                .Set("plan_summary", output["plan_summary"])
                .Set("audio_before", audioBefore)
                .Set("audio_after", audioAfter)
                .Set("subs_before", subsBefore)
                .Set("subs_after", subsAfter)
                .Set("remux_required", true)
                .Set("ffmpeg_argv", StringList(argv))
                .Set("audio_selection_notes", StringList(plan.AudioSelectionNotes))
                .Set("after_track_lines_meaning", "Remux failed partway; lines above were computed before ffmpeg — output file was not committed.");
        }

        var resolvedFinal = RemuxPassPaths.Resolve(final);
        output.Set("output_file", resolvedFinal);
        output.Set("output_replaced_existing", replacedExisting);
        output.Set("processing_output_folder_resolved", context.OutputDirectory);
        output.Set("hardware_method", hardware.Method.Length > 0 ? hardware.Method : null);
        output.Set("hardware_fell_back_to_software", hardware.FellBackToSoftware);
        output.Set("hardware_reason", hardware.Reason);
        output.Set("output_collision_policy", collision.Policy);
        output.Set("output_collision_action", collision.Action);
        // For the person asking "why is there no new output for this file".
        output.Set("output_collision_reason", collision.Reason);
        await _facts.RecordOutputCollisionAsync(relativeMediaPath, collision, context.Request.LibraryId, cancellationToken).ConfigureAwait(false);
        if (!collision.Wrote || replacedExisting)
        {
            output.Set("output_replacement_note", collision.Reason);
        }

        output.Set("after_track_lines_meaning",
            "Live remux finished; before = source probe; after = planned disposition (copy remux — " +
            "ffprobe of the written file was used for validation only).");
        try
        {
            var saved = new FileInfo(src).Length - new FileInfo(final).Length;
            if (saved > 0)
            {
                _metrics?.RecordModuleSavings("processing", saved);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        report?.Invoke(new PyDict()
            .Set("status", "finishing")
            .Set("percent", 100.0)
            .Set("eta_seconds", 0L)
            .Set("relative_media_path", relativeMediaPath)
            .Set("inspected_source_path", context.Inspected)
            .Set("output_file", resolvedFinal)
            .Set("media_scope", context.Scope)
            .Set("removed_audio", output["removed_audio"])
            .Set("removed_subtitles", output["removed_subtitles"])
            .Set("message", "The cleaned-up file was written. Weir is doing final safety checks."));
        await MigrateSidecarsBeforeCleanupAsync(src, final, sidecarPatterns, request.Runtime.PreserveOriginalTimestamps, output).ConfigureAwait(false);
        await HandleCleanupAfterSuccessAsync(context, output, final, cancellationToken).ConfigureAwait(false);
        await RunScopeOutputCleanupAsync(context, output, final, cancellationToken).ConfigureAwait(false);
        report?.Invoke(new PyDict()
            .Set("status", "finished")
            .Set("percent", 100.0)
            .Set("eta_seconds", 0L)
            .Set("relative_media_path", relativeMediaPath)
            .Set("inspected_source_path", context.Inspected)
            .Set("output_file", resolvedFinal)
            .Set("media_scope", context.Scope)
            .Set("message", "Finished processing this file."));
        return output;
    }

    /// <summary><c>{**base, **update}</c> for an ffmpeg progress block.</summary>
    private static PyDict ProgressWithUpdate(PyDict body, FfmpegProgressUpdate update) =>
        body
            .Set("percent", NullableFloat(update.Percent))
            .Set("eta_seconds", update.EtaSeconds)
            .Set("elapsed_seconds", update.ElapsedSeconds)
            .Set("processed_seconds", NullableFloat(update.ProcessedSeconds))
            .Set("speed", update.Speed)
            .Set("progress", update.Progress);
}
