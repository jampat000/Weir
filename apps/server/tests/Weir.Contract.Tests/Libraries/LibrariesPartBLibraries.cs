using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Creating a library that processes for real: watched, work and output folders under one root, nothing held back.</summary>
internal static class LibrariesPartBLibraries
{
    public static async Task<JsonObject> CreateAsync(WeirClient admin, string root)
    {
        var folders = new[] { "watched", "work", "output" }.Select(name => Path.Combine(root, name)).ToArray();
        foreach (var folder in folders)
        {
            Directory.CreateDirectory(folder);
        }

        var created = await admin.PostWithCsrfAsync($"{WeirClient.Api}/processing/libraries", new JsonObject
        {
            ["name"] = "Contract Movies",
            ["media_type"] = "movie",
            ["watched_folder"] = folders[0],
            ["work_folder"] = folders[1],
            ["output_folder"] = folders[2],
            ["ready_after_seconds"] = 0,
            ["min_file_size_mb"] = 0,
            ["skip_access_tests"] = true,
            ["retry_backoff_seconds"] = 1,
            // A workflow keeps 5 GB free by default; a small fixture file is processed whatever the drive holds.
            ["minimum_free_disk_space_mb"] = 0,
        });
        LibrariesPartBChecks.Status(created, HttpStatusCode.Created);
        return created.Fields;
    }
}
