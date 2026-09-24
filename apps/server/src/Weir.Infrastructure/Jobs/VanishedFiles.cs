using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// #645: a row still waiting, held, failed or cancelled whose file has not been on disk for <see cref="Grace"/>. The download
/// client removed it, a person deleted it, or the manager took it. The scan walks only files on disk, so nothing else would
/// ever judge that row again, and it would stay listed for ever. It is forgotten, as Forget does, with one Activity entry
/// saying why. A file with a pass queued or running is left to that pass, and a path that is now a folder on disk is kept.
/// Outcomes Weir reached (processed, passed through, rejected, skipped) stay as history.
/// </summary>
/// <remarks>
/// <para>Rows no scan has seen are included, and so are cancelled ones: a media manager's hand-off records its file on receipt,
/// before any scan sees it, and a cancelled hand-off is one Weir never started. Such a row counts from when it was last
/// written, so it gets the same grace before it is judged.</para>
/// <para>Every file is looked for with no transaction open; only the rows whose files are gone are then forgotten, a batch per
/// short transaction (#708). A row that changed in between (seen again, or moved on by a hand-off or a pass) is kept.</para>
/// </remarks>
public static class VanishedFiles
{
    /// <summary>How long a file must have been gone, since a scan last saw it, before Weir stops listing it. Longer than a
    /// download client takes to move a file, and than a share takes to come back from a blip.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    /// <summary>Rows forgotten per transaction.</summary>
    internal const int BatchSize = 250;

    private static readonly string[] WaitingStatuses =
    [
        ProcessingFileStatuses.Unprocessed, ProcessingFileStatuses.OnHold, ProcessingFileStatuses.OutOfSchedule,
        ProcessingFileStatuses.BlockedUpstream, ProcessingFileStatuses.ProcessingFailed, ProcessingFileStatuses.Cancelled,
    ];

    private sealed record WaitingRow(long Id, string RelativePath, string Status, object LastSeenStored);

    /// <summary>Forgets the library's rows whose files left the watched folder; returns their relative paths.</summary>
    public static async Task<List<string>> ForgetAsync(
        SqliteDatabase database, long libraryId, string watchedRoot, string mediaScope, DateTimeOffset now, string trigger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        var gone = (await OverdueRowsAsync(database, libraryId, now, cancellationToken).ConfigureAwait(false))
            .Where(row => !StillOnDisk(watchedRoot, row.RelativePath))
            .ToList();
        var forgotten = new List<string>();
        foreach (var batch in gone.Chunk(BatchSize))
        {
            await WriteLockTurns.TakeAsync(
                async () =>
                {
                    var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
                    await using (uow.ConfigureAwait(false))
                    {
                        foreach (var row in batch)
                        {
                            if (await ForgetOneAsync(uow, libraryId, mediaScope, row, trigger).ConfigureAwait(false))
                            {
                                forgotten.Add(row.RelativePath);
                            }
                        }

                        await uow.CommitAsync().ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        return forgotten;
    }

    private static async Task<List<WaitingRow>> OverdueRowsAsync(SqliteDatabase database, long libraryId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var statuses = WaitingStatuses.Select((status, index) => ($"@s{index}", (object?)status));
            var rows = await uow.QueryAsync(
                "SELECT id, relative_path, status, coalesce(last_seen_at, updated_at, created_at) FROM files WHERE library_id = @lib " +
                $"AND status IN ({string.Join(", ", WaitingStatuses.Select((_, index) => $"@s{index}"))})",
                reader => (Row: new WaitingRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetValue(3)), LastSeen: PythonTimestamps.Parse(reader.GetValue(3))),
                [("@lib", libraryId), .. statuses]).ConfigureAwait(false);
            var cutoff = now - Grace;
            return [.. rows.Where(row => row.LastSeen is { } seen && seen <= cutoff).Select(row => row.Row)];
        }
    }

    private static bool StillOnDisk(string watchedRoot, string relativePath)
    {
        string path;
        try
        {
            path = Path.GetFullPath(Path.Join(watchedRoot, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>Forgets one row, unless a pass now owns the file or the row changed since it was read.</summary>
    private static async Task<bool> ForgetOneAsync(UnitOfWork uow, long libraryId, string mediaScope, WaitingRow row, string trigger)
    {
        if (await ActiveRemuxPasses.ExistsForRelativePathAsync(uow, row.RelativePath, mediaScope, libraryId).ConfigureAwait(false))
        {
            return false;
        }

        var deleted = await uow.ExecuteAsync(
            "DELETE FROM files WHERE id = @id AND status = @status AND coalesce(last_seen_at, updated_at, created_at) = @seen",
            ("@id", row.Id),
            ("@status", row.Status),
            ("@seen", row.LastSeenStored)).ConfigureAwait(false);
        if (deleted == 0)
        {
            return false;
        }

        await SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFileLeftWatchedFolder,
            "processing",
            $"{MediaPathNames.Name(row.RelativePath, OperatingSystem.IsWindows())} left the watched folder before Weir finished with it, so it is no longer listed",
            PyJsonWriter.Dumps(
                new PyDict()
                    .Set("relative_media_path", row.RelativePath)
                    .Set("library_id", libraryId)
                    .Set("last_status", row.Status)
                    .Set("trigger", trigger)
                    .Set("result", "skipped"),
                PyJsonFormat.Compact))).ConfigureAwait(false);
        return true;
    }
}
