using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>Reads and settings calls the jobs tests share: the inspection list and a library's folders.</summary>
internal static class JobsApi
{
    public const string RemuxPassKind = "processing.file.remux_pass.v1";
    public const string ScanDispatchKind = "processing.watched_folder.remux_scan_dispatch.v1";

    private static readonly string[] AllStatuses =
        ["pending", "leased", "completed", "failed", "handler_ok_finalize_failed", "cancelled"];

    public static void Expect(WeirResponse response, HttpStatusCode status) =>
        Assert.True(response.Status == status, response.ToString());

    public static async Task<JsonObject> InspectionAsync(WeirClient client, int limit = 100, params string[] statuses)
    {
        var query = new List<(string, object)> { ("limit", limit) };
        query.AddRange(statuses.Select(status => ("status", (object)status)));
        var response = await client.GetAsync($"{WeirClient.Api}/processing/jobs/inspection", [.. query]);
        Expect(response, HttpStatusCode.OK);
        return response.Fields;
    }

    public static async Task<List<JsonObject>> AllJobsAsync(WeirClient client) =>
        [.. (await InspectionAsync(client, 100, AllStatuses))["jobs"]!.AsArray().Select(job => job!.AsObject())];

    public static async Task<JsonObject> JobByIdAsync(WeirClient client, int jobId)
    {
        var found = (await AllJobsAsync(client)).Where(job => (int)job["id"]! == jobId).ToList();
        Assert.True(found.Count > 0, $"job {jobId} is not in the inspection list");
        return found[0];
    }

    /// <summary>The library scope-only work resolves to: the first of that media type in display order.</summary>
    public static async Task<JsonObject> LibraryForScopeAsync(WeirClient client, string mediaType = "movie")
    {
        var response = await client.GetAsync($"{WeirClient.Api}/processing/libraries");
        Expect(response, HttpStatusCode.OK);
        var libraries = response.Elements
            .Select(library => library!.AsObject())
            .Where(library => (string)library["media_type"]! == mediaType)
            .OrderBy(library => (int)library["display_order"]!)
            .ThenBy(library => (int)library["id"]!)
            .ToList();
        Assert.True(libraries.Count > 0, $"no {mediaType} library");
        return libraries[0];
    }

    /// <summary>Saves one library whole, from its current values plus <paramref name="changes"/>.</summary>
    public static async Task<JsonObject> SaveLibraryAsync(
        WeirClient client, int libraryId, params (string Name, JsonNode? Value)[] changes)
    {
        var path = $"{WeirClient.Api}/processing/libraries/{libraryId}";
        var current = await client.GetAsync(path);
        Expect(current, HttpStatusCode.OK);
        var body = LibraryBodies.Unchanged(current.Fields);

        foreach (var (name, value) in changes)
        {
            body[name] = value;
        }

        var saved = await client.PutWithCsrfAsync(path, body);
        Expect(saved, HttpStatusCode.OK);
        return saved.Fields;
    }

    public static async Task<int> SetMovieFoldersAsync(WeirClient client, string? watched, string output, string? work = null)
    {
        var library = await LibraryForScopeAsync(client, "movie");
        var id = (int)library["id"]!;
        await SaveLibraryAsync(
            client, id, ("watched_folder", watched ?? ""), ("work_folder", work ?? ""), ("output_folder", output));
        return id;
    }
}
