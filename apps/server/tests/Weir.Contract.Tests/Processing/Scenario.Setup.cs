using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Processing;

internal sealed partial class Scenario
{
    /// <summary>The folder every scenario file lives under; the workflow's three folders are inside it.</summary>
    public string Root => _root.Path;

    /// <summary>Creates a movie workflow over <see cref="Folders"/>; <paramref name="overrides"/> replace or add to its settings.</summary>
    public async Task<JsonObject> CreateLibraryAsync(params (string Name, JsonNode? Value)[] overrides)
    {
        var body = new JsonObject
        {
            ["name"] = "Contract Movies",
            ["media_type"] = "movie",
            ["watched_folder"] = Folders.Watched,
            ["work_folder"] = Folders.Work,
            ["output_folder"] = Folders.Output,
            ["ready_after_seconds"] = 0,
            ["min_file_size_mb"] = 0,
            ["skip_access_tests"] = true,
            ["retry_backoff_seconds"] = 1,
            // A workflow keeps 5 GB free by default; a small fixture file is processed whatever the drive holds.
            ["minimum_free_disk_space_mb"] = 0,
        };
        foreach (var (name, value) in overrides)
        {
            body[name] = value;
        }

        var created = await Admin.PostWithCsrfAsync($"{Api}/processing/libraries", body);
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        return created.Fields;
    }

    public async Task<JsonObject> UpdateLibraryAsync(JsonObject library, params (string Name, JsonNode? Value)[] changes)
    {
        var path = $"{Api}/processing/libraries/{(int)library["id"]!}";
        var current = await Admin.GetAsync(path);
        Assert.True(current.Status == HttpStatusCode.OK, current.ToString());
        var body = LibraryBodies.Unchanged(current.Fields);
        foreach (var (name, value) in changes)
        {
            body[name] = value;
        }

        var updated = await Admin.PutWithCsrfAsync(path, body);
        Assert.True(updated.Status == HttpStatusCode.OK, updated.ToString());
        return updated.Fields;
    }

    public async Task<JsonObject> CreateConnectionAsync(FakeManager fake)
    {
        var created = await Admin.PostWithCsrfAsync($"{Api}/media-managers/connections", new JsonObject
        {
            ["kind"] = fake.Kind,
            ["name"] = $"Contract {char.ToUpperInvariant(fake.Kind[0])}{fake.Kind[1..]}",
            ["base_url"] = fake.BaseUrl,
            ["api_key"] = fake.ApiKey,
        });
        Assert.True(created.Status is HttpStatusCode.OK or HttpStatusCode.Created, created.ToString());
        return created.Fields;
    }

    /// <summary>A fake Deluno that manages this workflow, its connection, and the linked workflow.</summary>
    public async Task<(FakeManager Fake, JsonObject Library)> DelunoSetupAsync(
        string[]? capabilities = null, params (string Name, JsonNode? Value)[] library)
    {
        var fake = Own(FakeManager.StartDeluno(capabilities: capabilities ?? []));
        fake.Libraries.Add(DelunoLibraryManifest());
        var connection = await CreateConnectionAsync(fake);
        var created = await CreateLibraryAsync([("manager_connection_ids", new JsonArray(connection["id"]!.DeepClone())), .. library]);
        HandedOffLibraryId = (int)created["id"]!;
        return (fake, created);
    }

    /// <summary>A fake Radarr whose root folder is <c>library</c> under the scenario's folder, and a workflow it manages.</summary>
    public async Task<(FakeManager Fake, JsonObject Library)> RadarrSetupAsync(params (string Name, JsonNode? Value)[] library)
    {
        var fake = Own(FakeManager.StartArr("radarr", rootFolders: [Path.Combine(Root, "library")]));
        var connection = await CreateConnectionAsync(fake);
        var created = await CreateLibraryAsync([("manager_connection_ids", new JsonArray(connection["id"]!.DeepClone())), .. library]);
        return (fake, created);
    }

    public static JsonObject DelunoLibraryManifest() => new()
    {
        ["id"] = DelunoLibraryKey,
        ["name"] = "Movies",
        ["mediaType"] = "movies",
        ["rootPath"] = "/deluno/library/movies",
        ["importWorkflow"] = "refine-before-import",
        ["processorOutputPath"] = DelunoOutputRoot,
    };

    public async Task<JsonObject> SetPauseAsync(bool paused, bool keepLooking = true)
    {
        var response = await Admin.PutWithCsrfAsync($"{Api}/pause", new JsonObject { ["paused"] = paused, ["scan_while_paused"] = keepLooking });
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        Assert.Equal(paused, (bool)response.Fields["paused"]!);
        return response.Fields;
    }


    /// <summary>A hand-off from the fake Deluno: it names a file or release folder, and Weir queues it.</summary>
    public async Task<JsonObject> PostHandoffAsync(string handoffId, string sourcePath, string releaseName = "Contract.Release.2024")
    {
        var accepted = await Admin.PostAsync(
            $"{Api}/intake/webhook/deluno",
            new JsonObject
            {
                ["eventType"] = "deluno.processor-handoff",
                ["handoffId"] = handoffId,
                ["libraryId"] = DelunoLibraryKey,
                ["mediaType"] = "movies",
                ["sourcePath"] = sourcePath,
                ["releaseName"] = releaseName,
                ["callbackPath"] = EventsPath,
            },
            SecretHeader);
        Assert.True(accepted.Status == HttpStatusCode.OK, accepted.ToString());
        return accepted.Fields;
    }

    /// <summary>The fake Deluno's word on what became of a finished hand-off: <c>imported</c> or <c>not-imported</c>.</summary>
    public Task<WeirResponse> PostOutcomeAsync(string handoffId, string outcome, string? importedPath = null, string? reason = null) =>
        Admin.PostAsync(
            $"{Api}/intake/handoffs/deluno/{handoffId}/outcome",
            new JsonObject
            {
                ["outcome"] = outcome,
                ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                ["importedPath"] = importedPath,
                ["reason"] = reason,
            },
            SecretHeader);

    /// <summary>Asks to process one file now, as a person would from the Files page (no hand-off, no prior scan).</summary>
    public async Task EnqueueFilePassAsync(string relativeMediaPath, JsonObject library, bool passThroughUnchanged = false)
    {
        var body = new JsonObject { ["relative_media_path"] = relativeMediaPath, ["library_id"] = (int)library["id"]! };
        if (passThroughUnchanged)
        {
            body["pass_through_unchanged"] = true;
        }

        var response = await Admin.PostWithCsrfAsync($"{Api}/processing/jobs/file-remux-pass/enqueue", body);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
    }

    /// <summary>The scenario's release folder for a title, with the media file inside it; returns the file's path.</summary>
    public string WriteRelease(string releaseFolder, string fileName, byte[] content)
    {
        var folder = Path.Combine(Folders.Watched, releaseFolder);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, fileName);
        File.WriteAllBytes(file, content);
        return file;
    }

    private static IReadOnlyDictionary<string, string> SecretHeader { get; } =
        new Dictionary<string, string> { ["X-Webhook-Secret"] = WebhookSecret };
}
