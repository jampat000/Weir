using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Library folders through the API, and job rows through the database, for the System tests that need work to run.</summary>
internal static class SystemPartBLibraries
{
    private const string LibrariesPath = SystemPartBHelpers.Api + "/processing/libraries";

    // ProcessingLibraryOut fields that PUT /processing/libraries/{id} does not accept (it forbids extras).
    private static readonly string[] ReadOnlyFields =
    [
        "id", "display_order", "manager_coverage", "manager_coverage_detail", "discovered_from_connection_id",
        "discovered_library_key", "effective_max_concurrent_files", "active_job_count", "next_look_at", "periodic_scan",
        "next_scan_at", "updated_at",
    ];

    /// <summary>Points the library that scope-only work resolves to (the first movie library in display order) at these folders.</summary>
    public static async Task<long> SetMovieFoldersAsync(
        WeirClient client, string? watched, string output, string? work = null, long? scanIntervalSeconds = null)
    {
        var listing = await client.GetAsync(LibrariesPath);
        Assert.True(listing.Status == HttpStatusCode.OK, listing.ToString());
        var library = listing.Elements
            .Where(entry => (string?)entry!["media_type"] == "movie")
            .OrderBy(entry => (long)entry!["display_order"]!)
            .ThenBy(entry => (long)entry!["id"]!)
            .FirstOrDefault();
        Assert.True(library is not null, "no movie library");
        var id = (long)library["id"]!;

        var current = await client.GetAsync($"{LibrariesPath}/{id}");
        Assert.True(current.Status == HttpStatusCode.OK, current.ToString());
        var body = new JsonObject();
        foreach (var (name, value) in current.Fields.Where(field => !ReadOnlyFields.Contains(field.Key)))
        {
            body[name] = value?.DeepClone();
        }

        body["watched_folder"] = watched ?? string.Empty;
        body["work_folder"] = work ?? string.Empty;
        body["output_folder"] = output;
        if (scanIntervalSeconds is { } interval)
        {
            body["scan_interval_seconds"] = interval;
        }

        var saved = await client.PutWithCsrfAsync($"{LibrariesPath}/{id}", body);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
        return id;
    }

    /// <summary>One jobs row, last updated now.</summary>
    public static void InsertJob(SqliteConnection connection, string dedupeKey, string jobKind, string status)
    {
        var stamp = SeedSql.UtcText(DateTime.UtcNow);
        SeedSql.Execute(
            connection,
            "INSERT INTO jobs (dedupe_key, job_kind, status, created_at, updated_at) VALUES ($key, $kind, $status, $at, $at)",
            ("$key", dedupeKey),
            ("$kind", jobKind),
            ("$status", status),
            ("$at", stamp));
    }
}
