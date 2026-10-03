using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Stateless helpers the Processing and Library view contract tests share: seed rows, the viewer account, JSON reading.</summary>
public static class LibrariesPartAHelpers
{
    public const string Api = WeirClient.Api;
    public const string ViewerUsername = "bob";
    public const string ViewerPassword = "viewer-password-here";

    // Argon2id of ViewerPassword with the parameters the server hashes with (time 3, memory 65536 KiB, 1 lane, 32-byte hash, 16-byte salt).
    public const string ViewerPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=1$cZEfIo+vjAv5H5CYLMWHBQ$obuh2fmT1pyZ+KXQdfTWQ6EWmnEQB9tMdsQMbsPabyE";

    public const string RemuxPassJobKind = "processing.file.remux_pass.v1";
    public const string RemuxPassCompletedEvent = "processing.file_remux_pass_completed";

    public static string Now() => SeedSql.UtcText(DateTime.UtcNow);

    // --- accounts -------------------------------------------------------------------------------

    /// <summary>Seeds the viewer when the install has none; the server restarts on a new port, so make clients afterwards.</summary>
    public static async Task EnsureViewerAsync(WeirServer server)
    {
        await using var database = await server.StopForDatabaseAsync();
        InsertViewerIfMissing(database.Connection);
    }

    public static void InsertViewerIfMissing(SqliteConnection connection)
    {
        var existing = SeedSql.Scalar(connection, "SELECT COUNT(*) FROM users WHERE username = $name", ("$name", ViewerUsername));
        if (Convert.ToInt64(existing, CultureInfo.InvariantCulture) == 0)
        {
            SeedSql.InsertUser(connection, ViewerUsername, ViewerPasswordHash, "viewer");
        }
    }

    public static async Task<WeirClient> SignedInViewerAsync(WeirServer server)
    {
        var client = server.CreateClient();
        try
        {
            await client.LoginAsync(ViewerUsername, ViewerPassword);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    // --- requests -------------------------------------------------------------------------------

    /// <summary>A GET whose query is built from the pairs that carry a value; a null value leaves the parameter out.</summary>
    public static Task<WeirResponse> GetAsync(WeirClient client, string path, params (string Name, object? Value)[] query) =>
        client.GetAsync(path, query.Where(pair => pair.Value is not null).Select(pair => (pair.Name, pair.Value!)).ToArray());

    public static void ShouldBe(this WeirResponse response, HttpStatusCode expected) =>
        Assert.True(response.Status == expected, $"expected HTTP {(int)expected}, got {response}");

    public static void ShouldBe(this WeirResponse response, int expected) => response.ShouldBe((HttpStatusCode)expected);

    // --- JSON -----------------------------------------------------------------------------------

    public static JsonObject Obj(params (string Name, JsonNode? Value)[] fields)
    {
        var result = new JsonObject();
        foreach (var (name, value) in fields)
        {
            result[name] = value;
        }

        return result;
    }

    public static JsonArray Strings(IEnumerable<string> items) => new(items.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());

    public static List<string> StringList(JsonNode? array) =>
        array!.AsArray().Select(item => (string)item!).ToList();

    public static List<JsonObject> Objects(JsonNode? array) =>
        array!.AsArray().Select(item => item!.AsObject()).ToList();

    public static void AssertNullField(JsonObject node, string name)
    {
        Assert.True(node.ContainsKey(name), $"'{name}' is missing from {node.ToJsonString()}");
        Assert.Null(node[name]);
    }

    public static bool IsInteger(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out _);

    // --- seed rows ------------------------------------------------------------------------------

    public static long FirstLibraryId(SqliteConnection connection) =>
        Convert.ToInt64(SeedSql.Scalar(connection, "SELECT id FROM libraries ORDER BY id LIMIT 1"), CultureInfo.InvariantCulture);

    public static long LibraryIdNamed(SqliteConnection connection, string name) =>
        Convert.ToInt64(SeedSql.Scalar(connection, "SELECT id FROM libraries WHERE name = $name", ("$name", name)), CultureInfo.InvariantCulture);

    public static long InsertJob(
        SqliteConnection connection,
        string dedupeKey,
        string jobKind = RemuxPassJobKind,
        string status = "pending",
        JsonObject? payload = null)
    {
        var now = Now();
        return SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, created_at, updated_at) "
                + "VALUES ($key, $kind, $payload, $status, $now, $now)",
            ("$key", dedupeKey), ("$kind", jobKind), ("$payload", (payload ?? []).ToJsonString()), ("$status", status), ("$now", now));
    }

    public static long InsertFile(SqliteConnection connection, long libraryId, string relativePath, params (string Column, object? Value)[] columns)
    {
        var now = Now();
        var values = new Dictionary<string, object?>
        {
            ["library_id"] = libraryId,
            ["relative_path"] = relativePath,
            ["status"] = "unprocessed",
            ["status_reason"] = "",
            ["created_at"] = now,
            ["updated_at"] = now,
        };
        foreach (var (column, value) in columns)
        {
            values[column] = value;
        }

        return InsertRow(connection, "files", values);
    }

    /// <summary>Links the library to a media manager, which makes it a linked workflow rather than a Weir only one.</summary>
    public static void LinkLibraryToManager(SqliteConnection connection, long libraryId)
    {
        var connectionId = SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO media_manager_connections (kind, name, base_url) VALUES ($kind, $name, $url)",
            ("$kind", "radarr"), ("$name", "Radarr"), ("$url", "http://192.0.2.20:7878"));
        SeedSql.Execute(
            connection,
            "INSERT INTO library_manager_links (library_id, connection_id) VALUES ($library, $connection)",
            ("$library", libraryId), ("$connection", connectionId));
    }

    /// <summary>The cleaned copy Weir wrote for a file; with no extra columns no media manager has answered about it.</summary>
    public static long InsertHandback(SqliteConnection connection, long libraryId, string relativePath, params (string Column, object? Value)[] columns)
    {
        var values = new Dictionary<string, object?>
        {
            ["library_id"] = libraryId,
            ["relative_path"] = relativePath,
            ["output_path"] = $"/out/{relativePath}",
            ["output_size"] = 1,
            ["output_mtime_ns"] = 1,
            ["written_at"] = Now(),
        };
        foreach (var (column, value) in columns)
        {
            values[column] = value;
        }

        return InsertRow(connection, "handbacks", values);
    }

    public static long InsertActivityEvent(SqliteConnection connection, string eventType, string module, string title, string? detail = null) =>
        SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO activity_events (event_type, module, title, detail, created_at) VALUES ($type, $module, $title, $detail, $now)",
            ("$type", eventType), ("$module", module), ("$title", title), ("$detail", detail), ("$now", Now()));

    private static long InsertRow(SqliteConnection connection, string table, Dictionary<string, object?> values)
    {
        var columns = values.Keys.ToList();
        var markers = columns.Select((_, index) => $"$p{index}").ToList();
        var parameters = values.Values.Select((value, index) => ($"$p{index}", value)).ToArray();
        return SeedSql.InsertAndGetId(
            connection,
            $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", markers)})",
            parameters);
    }
}
