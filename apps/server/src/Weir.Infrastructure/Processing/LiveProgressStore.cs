using System.Globalization;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>How far the pass currently working on a file has got.</summary>
/// <param name="Percent">How much of the file has been written, 0 to 100.</param>
/// <param name="Message">What the pass says it is doing, in its own words.</param>
/// <param name="EtaSeconds">The pass's own estimate of the time left.</param>
/// <param name="Status"><c>processing</c> while the file is written; <c>finishing</c> during the final checks and hand-back.</param>
/// <param name="Speed">ffmpeg's speed as it reports it, for example <c>148x</c>.</param>
/// <param name="ElapsedSeconds">How long the pass has been writing.</param>
/// <param name="RemovedAudio">The audio tracks this pass is taking out, as the plan describes each one.</param>
/// <param name="RemovedSubtitles">The subtitle tracks this pass is taking out.</param>
public sealed record LiveProgress(
    double? Percent,
    string? Message,
    double? EtaSeconds,
    string Status,
    string? Speed,
    double? ElapsedSeconds,
    IReadOnlyList<string> RemovedAudio,
    IReadOnlyList<string> RemovedSubtitles);

/// <summary>
/// Reads the live per-file progress Activity row a running pass keeps updated (#463). Read-only: nothing here
/// writes a second source of truth.
/// </summary>
/// <remarks>
/// A pass inserts one row and rewrites it on every report, so the row's <c>created_at</c> is when the pass
/// started, not when it last reported. Judging staleness on it would drop the live progress of any pass longer
/// than <see cref="StaleAfter"/>, so each report carries <c>reported_at</c>
/// (<see cref="RemuxPass.ActivityProgressReporter"/>) and a row is stale when its last report is. Rows without
/// that field, written by earlier releases, fall back to <c>created_at</c>.
/// </remarks>
public static class LiveProgressStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>How far back to look for a pass that is still running. Generous: nothing is shown from it unless it reported within <see cref="StaleAfter"/>.</summary>
    private static readonly TimeSpan LongestPass = TimeSpan.FromHours(12);

    private static readonly HashSet<string> LiveStatuses = new(StringComparer.Ordinal) { "processing", "finishing" };
    private const int MaxRows = 64;

    /// <summary>The newest progress rows of the last <see cref="LongestPass"/>.</summary>
    internal const string RecentProgressSql =
        "SELECT created_at, detail FROM activity_events WHERE event_type = @type AND created_at >= @since ORDER BY id DESC LIMIT @max_rows";

    /// <summary>Maps <c>relative_media_path</c> to the progress of the pass running on it.</summary>
    public static async Task<Dictionary<string, LiveProgress>> ByPathAsync(UnitOfWork uow, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var since = now - LongestPass;
        var rows = await uow.QueryAsync(
            RecentProgressSql,
            reader => (Created: SqliteValues.GetDateTime(reader, 0), Detail: reader.IsDBNull(1) ? null : reader.GetString(1)),
            ("@type", ActivityEventTypes.ProcessingFileProcessingProgress),
            ("@since", SqliteValues.ToSqlite(PyDateTime.FromUtc(since.UtcDateTime))),
            ("@max_rows", MaxRows)).ConfigureAwait(false);

        var result = new Dictionary<string, LiveProgress>(StringComparer.Ordinal);
        foreach (var (created, raw) in rows)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            PyJson parsed;
            try
            {
                parsed = PyJsonParser.Parse(raw);
            }
            catch (PyJsonDecodeException)
            {
                continue;
            }

            if (parsed is not PyDict payload)
            {
                continue;
            }

            var status = payload.TryGetValue("status", out var statusValue) && statusValue is PyStr statusStr ? statusStr.Value.Trim().ToLowerInvariant() : string.Empty;
            if (!LiveStatuses.Contains(status))
            {
                continue;
            }

            var lastReported = ReportedAt(payload) ?? new DateTimeOffset(created.AsUtc, TimeSpan.Zero);
            if (now - lastReported > StaleAfter)
            {
                continue;
            }

            var path = payload.TryGetValue("relative_media_path", out var pathValue) && pathValue is PyStr pathStr ? pathStr.Value.Trim() : string.Empty;
            if (path.Length == 0 || result.ContainsKey(path))
            {
                continue;
            }

            result[path] = new LiveProgress(
                CoercePercent(payload.TryGetValue("percent", out var p) ? p : null),
                Text(payload, "message"),
                CoerceSeconds(payload.TryGetValue("eta_seconds", out var e) ? e : null),
                status,
                Text(payload, "speed"),
                CoerceSeconds(payload.TryGetValue("elapsed_seconds", out var el) ? el : null),
                Strings(payload, "removed_audio"),
                Strings(payload, "removed_subtitles"));
        }

        return result;
    }

    private static DateTimeOffset? ReportedAt(PyDict payload) =>
        payload.TryGetValue("reported_at", out var value) && value is PyStr text
        && DateTimeOffset.TryParse(text.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string? Text(PyDict payload, string key) =>
        payload.TryGetValue(key, out var value) && value is PyStr text && text.Value.Trim().Length > 0 ? text.Value.Trim() : null;

    private static List<string> Strings(PyDict payload, string key) =>
        payload.TryGetValue(key, out var value) && value is PyList list
            ? list.Items.OfType<PyStr>().Select(item => item.Value).Where(item => item.Trim().Length > 0).ToList()
            : [];

    private static double? CoercePercent(PyJson? value)
    {
        var number = Number(value);
        return number is null || double.IsNaN(number.Value) ? null : Math.Clamp(number.Value, 0.0, 100.0);
    }

    private static double? CoerceSeconds(PyJson? value)
    {
        var number = Number(value);
        return number is null || double.IsNaN(number.Value) || number.Value < 0 ? null : number.Value;
    }

    private static double? Number(PyJson? value) => value switch
    {
        PyInt i => (double)i.Value,
        PyFloat f => f.Value,
        PyBool b => b.Value ? 1.0 : 0.0,
        PyStr s when double.TryParse(s.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
