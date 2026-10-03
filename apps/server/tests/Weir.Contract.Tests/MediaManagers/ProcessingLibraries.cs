using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>A library's watched and output folders on disk.</summary>
internal sealed record LibraryFolders(string Watched, string Output)
{
    public static LibraryFolders Make(string root)
    {
        var folders = new LibraryFolders(Path.Combine(root, "watched"), Path.Combine(root, "output"));
        Directory.CreateDirectory(folders.Watched);
        Directory.CreateDirectory(folders.Output);
        return folders;
    }
}

/// <summary>Steps on Processing libraries, all through the public API.</summary>
internal static class ProcessingLibraries
{
    private const string Route = $"{WeirClient.Api}/processing/libraries";

    private static readonly string[] WritableFields =
    [
        "name", "media_type", "enabled", "watched_folder", "work_folder", "output_folder", "media_extensions_csv",
        "exclude_markers_csv", "include_patterns_csv", "exclude_patterns_csv", "min_file_size_mb", "max_file_size_mb",
        "rejected_file_action", "ready_after_seconds", "exclude_hidden", "top_level_only", "sidecar_patterns_csv",
        "preserve_original_timestamps", "output_collision_policy", "ffmpeg_strictness", "scan_interval_seconds",
        "ignore_size_changes", "skip_access_tests", "max_attempts", "retry_backoff_seconds", "retry_execution_failures",
        "failure_policy", "retry_preflight_failures", "schedule_grid", "file_system_events_enabled", "schedule_enabled",
        "schedule_hours_limited", "schedule_days", "schedule_start", "schedule_end", "max_concurrent_files", "priority",
        "rule_set_id", "manager_connection_ids", "remove_original_after_success",
    ];

    public static async Task<JsonArray> ListAsync(WeirClient client)
    {
        var listed = await client.GetAsync(Route);
        Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
        return listed.Elements;
    }

    public static async Task<JsonObject> CreateAsync(
        WeirClient client, string name, string mediaType, LibraryFolders folders, JsonObject? overrides = null)
    {
        var created = await client.PostWithCsrfAsync(Route, JsonFields.Merge(
            new JsonObject
            {
                ["name"] = name,
                ["media_type"] = mediaType,
                ["watched_folder"] = folders.Watched,
                ["output_folder"] = folders.Output,
            },
            overrides));
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        return created.Fields;
    }

    /// <summary>A PUT is a whole-library save, so this starts from the library as it is and changes only <paramref name="changes"/>.</summary>
    public static async Task<JsonObject> UpdateAsync(WeirClient client, JsonObject library, JsonObject changes)
    {
        var body = new JsonObject();
        foreach (var field in WritableFields.Where(library.ContainsKey))
        {
            body[field] = library[field]?.DeepClone();
        }

        var saved = await client.PutWithCsrfAsync($"{Route}/{JsonFields.Id(library)}", JsonFields.Merge(body, changes));
        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
        return saved.Fields;
    }

    /// <summary>The library called <paramref name="name"/> with these folders: a fresh install's seeded one is pointed at them.</summary>
    public static async Task<JsonObject> EnsureAsync(
        WeirClient client, string name, string mediaType, LibraryFolders folders, JsonObject? overrides = null)
    {
        var wanted = JsonFields.Merge(
            new JsonObject { ["watched_folder"] = folders.Watched, ["output_folder"] = folders.Output }, overrides);
        foreach (var node in await ListAsync(client))
        {
            var row = node!.AsObject();
            if ((string?)row["name"] != name)
            {
                continue;
            }

            var alreadyThere = wanted.All(field => row[field.Key]?.ToJsonString() == field.Value?.ToJsonString());
            return alreadyThere
                ? row
                : await UpdateAsync(client, row, JsonFields.Merge(new JsonObject { ["media_type"] = mediaType }, wanted));
        }

        return await CreateAsync(client, name, mediaType, folders, overrides);
    }
}
