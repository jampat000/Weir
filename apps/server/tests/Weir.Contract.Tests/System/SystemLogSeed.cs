using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Rows for the System, Logs contract tests, written straight into the database and the server log.</summary>
internal static class SystemLogSeed
{
    // Signing in and starting the server write rows of their own at the real time; everything seeded here is two
    // days back, and each request is held to that day so it sees only what the test put there.
    public static readonly DateTime SeededAt = WholeSeconds(DateTime.UtcNow.AddDays(-2));

    public static readonly (string Name, object Value)[] Window =
    [
        ("from", Iso(SeededAt.AddHours(-1))),
        ("to", Iso(SeededAt.AddHours(1))),
    ];

    public static DateTime At(int minutes) => SeededAt.AddMinutes(minutes);

    /// <summary>The log request for the seeded day, plus these parameters.</summary>
    public static (string Name, object Value)[] InWindow(params (string Name, object Value)[] parameters) =>
        [.. Window, .. parameters];

    public static string LogLine(
        DateTime at, string level, string logger, string message, params (string Name, JsonNode? Value)[] extra)
    {
        var entry = new JsonObject
        {
            ["timestamp"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
            ["level"] = level,
            ["logger"] = logger,
            ["message"] = message,
            ["source"] = null,
            ["detail"] = null,
            ["correlation_id"] = null,
            ["job_id"] = null,
        };
        foreach (var (name, value) in extra)
        {
            entry[name] = value;
        }

        return entry.ToJsonString();
    }

    /// <summary>Appends lines to the server's log file; the server must be stopped, since it holds the file open while it runs.</summary>
    public static void AppendToServerLog(WeirServer server, IEnumerable<string> lines)
    {
        var logs = Path.Combine(server.Home, "logs");
        Directory.CreateDirectory(logs);
        File.AppendAllLines(Path.Combine(logs, "weir.log"), lines);
    }

    public static void InsertEvent(
        SqliteConnection connection,
        DateTime at,
        string eventType,
        string title,
        string result,
        string? trigger = null,
        long? libraryId = null,
        string? relativePath = null) =>
        SeedSql.Execute(
            connection,
            "INSERT INTO activity_events (created_at, event_type, module, title, result, \"trigger\", library_id, relative_path) "
                + "VALUES ($at, $type, 'processing', $title, $result, $trigger, $library, $path)",
            ("$at", SeedSql.UtcText(at)),
            ("$type", eventType),
            ("$title", title),
            ("$result", result),
            ("$trigger", trigger),
            ("$library", libraryId),
            ("$path", relativePath));

    public static long InsertJob(
        SqliteConnection connection,
        DateTime at,
        string key,
        string kind,
        string status,
        string? lastError = null,
        long? libraryId = null) =>
        SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, attempt_count, max_attempts, last_error, created_at, updated_at) "
                + "VALUES ($key, $kind, $payload, $status, 1, 3, $error, $at, $at)",
            ("$key", key),
            ("$kind", kind),
            ("$payload", libraryId is { } id ? new JsonObject { ["library_id"] = id }.ToJsonString() : null),
            ("$status", status),
            ("$error", lastError),
            ("$at", SeedSql.UtcText(at)));

    private static DateTime WholeSeconds(DateTime moment) => new(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    private static string Iso(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+00:00";
}
