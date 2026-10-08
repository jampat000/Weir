using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Observability;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// One processing per source file. A source is the file at a path with a size and a modification time; once a pass has
/// cleaned it and written its copy, every later route that would clean the same source again (a resent or replayed
/// hand-off, a requeue, a scan, a pass queued before this check existed) settles here instead, as a skip with a reason.
/// A different source at the same path, or a different path, is a new opportunity and goes through.
/// </summary>
public static class CleanedSources
{
    /// <summary>
    /// What Weir already did with this source, or null when it should be processed: the file is not there to measure, it was
    /// not cleaned (or has changed since), or the copy it wrote is gone and nobody collected it.
    /// </summary>
    public static Task<CleanedEarlier?> FindAsync(UnitOfWork uow, long libraryId, string watchedFolder, string relativePath) =>
        FindAsync(uow, libraryId, relativePath, recorded => SourceIsUnchanged(watchedFolder, relativePath, recorded));

    /// <summary>
    /// What Weir already did with a file whose original is no longer in the watched folder: there is no source to compare, and
    /// nothing left to process, so the earlier cleaning is the answer whenever its copy is still there or a manager collected it.
    /// </summary>
    public static Task<CleanedEarlier?> FindForMissingOriginalAsync(UnitOfWork uow, long libraryId, string relativePath) =>
        FindAsync(uow, libraryId, relativePath, recorded => recorded.Status == ProcessingFileStatuses.Processed);

    private static async Task<CleanedEarlier?> FindAsync(UnitOfWork uow, long libraryId, string relativePath, Func<ProcessingFileRecord, bool> sourceIsUnchanged)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var earlier = await uow.QuerySingleAsync(
            "SELECT f.status, f.processed_source_size, f.processed_source_mtime_ns, h.output_path, h.written_at, h.outcome, h.outcome_by, h.outcome_at " +
            "FROM files f JOIN handbacks h ON h.library_id = f.library_id AND h.relative_path = f.relative_path " +
            "WHERE f.library_id = $library AND f.relative_path = $path",
            reader => new
            {
                Record = new ProcessingFileRecord
                {
                    RelativePath = relativePath,
                    Status = SqliteValues.GetString(reader, 0),
                    ProcessedSourceSize = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    ProcessedSourceMtimeNs = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                },
                OutputPath = SqliteValues.GetString(reader, 3),
                WrittenAt = TimestampColumns.Parse(reader.GetValue(4)),
                Outcome = SqliteValues.GetStringOrNull(reader, 5),
                OutcomeBy = SqliteValues.GetStringOrNull(reader, 6),
                OutcomeAt = TimestampColumns.Parse(reader.GetValue(7)),
            },
            ("$library", libraryId),
            ("$path", relativePath)).ConfigureAwait(false);
        if (earlier is null || earlier.WrittenAt is not { } writtenAt || !sourceIsUnchanged(earlier.Record))
        {
            return null;
        }

        if (File.Exists(earlier.OutputPath))
        {
            return new CleanedEarlier(earlier.OutputPath, writtenAt, Collected: false, Manager: null, CollectedAt: null);
        }

        return earlier.Outcome == HandbackRules.Imported
            ? new CleanedEarlier(earlier.OutputPath, writtenAt, Collected: true, earlier.OutcomeBy, earlier.OutcomeAt)
            : null;
    }

    /// <summary>
    /// The one Activity line for a repeat that was left alone, grey ("skipped"): never a failure, and never something a
    /// person has to act on.
    /// </summary>
    public static Task<long> RecordSkipAsync(UnitOfWork uow, long libraryId, string relativePath, CleanedEarlier earlier, string trigger)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(earlier);
        var title = earlier.Title(MediaPathNames.Name(relativePath, OperatingSystem.IsWindows()));
        var detail = OperatorMessages.ActivityDetailEnvelope("processing", "skip_repeat", trigger, "skipped", userMessage: earlier.Reason)
            .Set("relative_media_path", relativePath)
            .Set("library_id", libraryId)
            .Set("output_file", earlier.OutputPath)
            .Set("cleaned_at", earlier.CleanedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .Set("collected", earlier.Collected);
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileSkippedRepeat,
            "processing",
            title,
            WireStrings.Slice(WireJsonWriter.Dumps(detail, WireJsonFormat.Compact), 10_000)));
    }

    private static bool SourceIsUnchanged(string watchedFolder, string relativePath, ProcessingFileRecord recorded)
    {
        try
        {
            var fingerprint = SourceFiles.Fingerprint(RemuxPassPaths.ResolveMediaFileUnderRoot(watchedFolder, relativePath));
            return ProcessedSourceRules.IsSameCleanedFile(recorded, fingerprint.SizeBytes, fingerprint.ModifiedTimeNs);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
