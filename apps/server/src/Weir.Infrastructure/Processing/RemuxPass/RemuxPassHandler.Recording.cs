using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    /// <summary>
    /// Writes the processing record and the Activity row together so they cannot disagree. A progress row
    /// already started for the pass becomes the completed row.
    /// </summary>
    internal async Task RecordAsync(WireObject payload, long? activityId = null)
    {
        var detail = RemuxPassVisibility.ActivityDetail(payload);
        var title = RemuxPassVisibility.ActivityTitle(payload);
        try
        {
            await LockedWrites.RunAsync(
                _database,
                async uow =>
                {
                    if (payload.Get("relative_media_path") is WireString rel && WireStrings.Strip(rel.Value).Length > 0)
                    {
                        try
                        {
                            await RemuxPassFileState.RecordFileLogAsync(uow, rel.Value, title, payload, _time.GetUtcNow()).ConfigureAwait(false);
                        }
                        catch (SqliteException exception) when (LockedWrites.IsLock(exception))
                        {
                            throw;
                        }
                        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
                        {
                            _logger.LogError(exception, "Weir could not write the processing record for {Path}.", rel.Value);
                        }
                    }

                    if (activityId is { } id &&
                        await SqliteActivityWriter.UpdateAsync(uow, id, ActivityEventTypes.ProcessingFileRemuxPassCompleted, title, detail).ConfigureAwait(false))
                    {
                        return;
                    }

                    await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(ActivityEventTypes.ProcessingFileRemuxPassCompleted, "processing", title, detail))
                        .ConfigureAwait(false);
                },
                _logger,
                "processing record").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // Activity is observability, never a prerequisite for a safe media mutation.
            _logger.LogWarning(exception, "Weir could not save the processing record; the pass result remains authoritative.");
        }
    }

    /// <summary>Applies an opt-in rejection deletion, after the rejection was recorded.</summary>
    private async Task FinishRejectedInputCleanupAsync(WireObject result, long? libraryId, string mediaScope)
    {
        if (result.Get("rejection_kind") is not { IsTruthy: true } || result.Get("rejected_file_action") is not WireString { Value: "delete_file" })
        {
            return;
        }

        if (result.Get("processing_watched_folder_resolved") is not WireString watched || result.Get("inspected_source_path") is not WireString inspected)
        {
            result.Set("rejected_cleanup_status", "not_deleted");
            result.Set("rejected_cleanup_detail", "Weir did not delete the rejected file because its verified watched-folder path was unavailable.");
        }
        else
        {
            var cleanup = RemuxPassPaths.CleanupRejectedFile(watched.Value, inspected.Value, "delete_file");
            result.Set("rejected_cleanup_status", cleanup.Deleted ? "deleted" : "not_deleted");
            result.Set("rejected_cleanup_detail", cleanup.Detail);
        }

        await ApplyFileOutcomeStateAsync(result, libraryId, mediaScope, null).ConfigureAwait(false);
        if (result.Get("relative_media_path") is not WireString rel || WireStrings.Strip(rel.Value).Length == 0)
        {
            return;
        }

        await LockedWrites.RunAsync(
            _database,
            uow => RemuxPassFileState.RecordFileLogAsync(uow, rel.Value, "Rejected file cleanup finished", result, _time.GetUtcNow()),
            _logger,
            "rejected file cleanup record").ConfigureAwait(false);
    }
}
