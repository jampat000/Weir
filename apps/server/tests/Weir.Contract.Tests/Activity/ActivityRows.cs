using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Activity;

/// <summary>The filterable facts the server keeps beside an event's detail.</summary>
internal sealed record EventFacts(string? Trigger = null, string? Result = null, int? LibraryId = null, string? RelativePath = null);

/// <summary>Rows the activity tests seed straight into the schema, because no API creates them.</summary>
internal static class ActivityRows
{
    public const string ViewerUsername = "bob";
    public const string ViewerPassword = "viewer-password-here";

    // Argon2id (PHC format, as users.password_hash holds it) of ViewerPassword: the suite has no hasher of its own.
    private const string ViewerPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$1FAZDPY1Q+jK6pqz4PUmDQ$xdxYW1ObpjuQPUugXzhpJTy8i3qLJQpca61EfWZ6gJI";

    private const string ProcessingEventType = "processing.file_remux_pass_completed";
    private const string ProcessingModule = "processing";
    private const int OldestResultSecondsAgo = 10;

    /// <summary>The four Processing results the history tests filter on, with the facts the server lifts from each detail.</summary>
    private static readonly HistoryRow[] HistoryRows =
    [
        new("Heat was handed back", Detail("scheduled", 1, "Heat/heat.mkv"), new("scheduled", "success", 1, "Heat/heat.mkv")),
        new("Heat failed", Failed(Detail("retry", 1, "Heat/heat.mkv")), new("retry", "failed", 1, "Heat/heat.mkv")),
        new("Alien processed", Detail("manual", 1, "Alien/alien.mkv"), new("manual", "success", 1, "Alien/alien.mkv")),
        new("Show processed", Detail("webhook", 2, "Show/S01E01.mkv"), new("webhook", "success", 2, "Show/S01E01.mkv")),
    ];

    /// <summary>One <c>activity_events</c> row with its filterable facts, the way the server writes them (#469).</summary>
    public static long InsertEvent(
        SqliteConnection connection,
        string eventType,
        string module,
        string title,
        string? detail = null,
        DateTime? createdAt = null,
        EventFacts? facts = null) =>
        SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO activity_events (event_type, module, title, detail, created_at, \"trigger\", result, library_id, relative_path) "
                + "VALUES ($eventType, $module, $title, $detail, $createdAt, $trigger, $result, $libraryId, $relativePath)",
            ("$eventType", eventType),
            ("$module", module),
            ("$title", title),
            ("$detail", detail),
            ("$createdAt", SeedSql.UtcText(createdAt ?? DateTime.UtcNow)),
            ("$trigger", facts?.Trigger),
            ("$result", facts?.Result),
            ("$libraryId", facts?.LibraryId),
            ("$relativePath", facts?.RelativePath));

    public static void EnsureViewer(SqliteConnection connection)
    {
        var existing = SeedSql.Scalar(connection, "SELECT COUNT(*) FROM users WHERE username = $username", ("$username", ViewerUsername));
        if (Convert.ToInt64(existing, CultureInfo.InvariantCulture) == 0)
        {
            SeedSql.InsertUser(connection, ViewerUsername, ViewerPasswordHash);
        }
    }

    /// <summary>Replaces all activity and per-file processing records with the four Processing results.</summary>
    public static void SeedHistory(SqliteConnection connection)
    {
        var now = DateTime.UtcNow;
        SeedSql.Execute(connection, "DELETE FROM activity_events");
        SeedSql.Execute(connection, "DELETE FROM file_logs");
        for (var offset = 0; offset < HistoryRows.Length; offset++)
        {
            var row = HistoryRows[offset];
            InsertEvent(
                connection,
                ProcessingEventType,
                ProcessingModule,
                row.Title,
                row.Detail.ToJsonString(),
                now - TimeSpan.FromSeconds(OldestResultSecondsAgo - offset),
                row.Facts);
        }

        foreach (var path in new[] { "Heat/heat.mkv", "Alien/alien.mkv" })
        {
            SeedSql.Execute(
                connection,
                "INSERT INTO file_logs (library_id, relative_path, title, recorded_at) VALUES (1, $path, 'pass', $recordedAt)",
                ("$path", path),
                ("$recordedAt", SeedSql.UtcText(now)));
        }

        EnsureViewer(connection);
    }

    /// <summary>A finished job of the given age, whose removal shows the retention pass has run.</summary>
    public static void InsertCompletedJob(SqliteConnection connection, string dedupeKey, TimeSpan age)
    {
        var stamp = SeedSql.UtcText(DateTime.UtcNow - age);
        SeedSql.Execute(
            connection,
            "INSERT INTO jobs (dedupe_key, job_kind, status, created_at, updated_at) "
                + "VALUES ($dedupeKey, 'processing.work_temp_stale_sweep.v1', 'completed', $stamp, $stamp)",
            ("$dedupeKey", dedupeKey),
            ("$stamp", stamp));
    }

    private static JsonObject Detail(string trigger, int libraryId, string relativeMediaPath) => new()
    {
        ["trigger"] = trigger,
        ["library_id"] = libraryId,
        ["relative_media_path"] = relativeMediaPath,
    };

    private static JsonObject Failed(JsonObject detail)
    {
        detail["ok"] = false;
        return detail;
    }

    private sealed record HistoryRow(string Title, JsonObject Detail, EventFacts Facts);
}
