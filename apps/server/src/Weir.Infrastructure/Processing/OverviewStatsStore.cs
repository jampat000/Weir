using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The custody-screen overview counters.</summary>
public sealed record ProcessingOverviewStats(
    int WindowDays,
    long FilesProcessed,
    long FilesFailed,
    double SuccessRatePercent,
    long OutputWrittenCount,
    long AlreadyOptimizedCount,
    long NetSpaceSavedBytes,
    double NetSpaceSavedPercent);

/// <summary>The Processing overview's statistics.</summary>
public sealed class OverviewStatsStore
{
    private const string RemuxPassJobKind = "processing.file.remux_pass.v1";
    private const string OutcomeLiveOutputWritten = "live_output_written";
    private const string OutcomeLiveSkippedNotRequired = "live_skipped_not_required";

    /// <summary>
    /// A file still known to Weir: <c>files</c> keeps one row per file for as long as Weir has a record of it, and
    /// only forgetting it (the Files or History "Remove from list" action) deletes that row. Counting a result
    /// only while its file passes this check is how forgetting a file also drops it from these counters, without
    /// deleting the Activity event or job row itself — both stay for System's own history and jobs list, and the
    /// job row's dedupe key keeps doing its job of refusing a second pass for the same file.
    /// </summary>
    private const string FileStillKnownByRemuxPayload =
        "EXISTS (SELECT 1 FROM files WHERE files.relative_path = json_extract(jobs.payload_json, '$.relative_media_path') " +
        "AND files.library_id = json_extract(jobs.payload_json, '$.library_id'))";

    private const string FileStillKnownByActivityColumns =
        "EXISTS (SELECT 1 FROM files WHERE files.relative_path = activity_events.relative_path AND files.library_id = activity_events.library_id)";

    /// <summary>The completed passes of the window, whose details carry the sizes and outcomes.</summary>
    internal const string RecentResultsSql =
        "SELECT detail FROM activity_events WHERE event_type = @type AND created_at >= @since AND " + FileStillKnownByActivityColumns;

    public async Task<ProcessingOverviewStats> BuildAsync(UnitOfWork uow, int windowDays, TimeProvider time)
    {
        var days = Math.Max(1, windowDays);
        var since = time.GetUtcNow().AddDays(-days);
        var sinceParam = SqliteValues.ToSqlite(Timestamp.FromUtc(since.UtcDateTime));

        var failed = await uow.CountAsync(
            "SELECT COUNT(*) FROM jobs WHERE job_kind = @kind AND status IN ('failed', 'handler_ok_finalize_failed') AND updated_at >= @since AND " +
            FileStillKnownByRemuxPayload,
            ("@kind", RemuxPassJobKind), ("@since", sinceParam)).ConfigureAwait(false);

        var detailRows = await uow.QueryAsync(
            RecentResultsSql,
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            ("@type", ActivityEventTypes.ProcessingFileRemuxPassCompleted), ("@since", sinceParam)).ConfigureAwait(false);

        long outputWrittenCount = 0;
        long alreadyOptimizedCount = 0;
        long totalSourceBytes = 0;
        long totalOutputBytes = 0;
        foreach (var raw in detailRows)
        {
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            WireValue parsed;
            try
            {
                parsed = WireJsonParser.Parse(raw);
            }
            catch (WireJsonDecodeException)
            {
                continue;
            }

            if (parsed is not WireObject payload)
            {
                continue;
            }

            var outcome = payload.TryGetValue("outcome", out var outcomeValue) && outcomeValue is WireString outcomeStr ? outcomeStr.Value.Trim() : string.Empty;
            if (outcome == OutcomeLiveOutputWritten)
            {
                outputWrittenCount++;
                var sourceBytes = JsonInt(payload.TryGetValue("source_size_bytes", out var sb) ? sb : null);
                var outputBytes = JsonInt(payload.TryGetValue("output_size_bytes", out var ob) ? ob : null);
                if (sourceBytes is not null && outputBytes is not null)
                {
                    totalSourceBytes += Math.Max(0, sourceBytes.Value);
                    totalOutputBytes += Math.Max(0, outputBytes.Value);
                }
            }
            else if (outcome == OutcomeLiveSkippedNotRequired)
            {
                if (payload.TryGetValue("output_copied_without_remux", out var copied) && copied is WireBool { Value: true })
                {
                    alreadyOptimizedCount++;
                }
            }
        }

        var completed = outputWrittenCount + alreadyOptimizedCount;
        var terminal = completed + failed;
        var rate = terminal > 0 ? Math.Round((double)completed / terminal * 100.0, 1) : 0.0;
        var netSaved = totalSourceBytes - totalOutputBytes;
        var netSavedPercent = totalSourceBytes > 0 ? Math.Round((double)netSaved / totalSourceBytes * 100.0, 1) : 0.0;

        return new ProcessingOverviewStats(days, completed, failed, rate, outputWrittenCount, alreadyOptimizedCount, netSaved, netSavedPercent);
    }

    private static long? JsonInt(WireValue? value) => value switch
    {
        WireInteger i => (long)i.Value,
        WireNumber f => (long)f.Value,
        WireString s when long.TryParse(s.Value.Trim(), out var parsed) => parsed,
        WireString s when double.TryParse(s.Value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble) => (long)parsedDouble,
        _ => null,
    };
}
