using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Jobs;

/// <summary>
/// A server that works files, a library that a fake Deluno manages, and a Deluno hand-off: what the lease-renewal
/// scenario needs, every step through the public API.
/// </summary>
internal static class JobsHandoff
{
    public const string WebhookSecret = "contract-webhook-secret-0123456789";
    public const string DelunoLibraryKey = "5f2c0a9e";
    public const string EventsPath = "/api/integrations/processors/events";

    private const string DelunoOutputRoot = "/deluno/processed/movies";

    /// <summary>The folders a library watches, works in and writes to.</summary>
    public sealed record Folders(string Watched, string Work, string Output)
    {
        public static Folders Make(string root) => new(
            Directory.CreateDirectory(Path.Combine(root, "watched")).FullName,
            Directory.CreateDirectory(Path.Combine(root, "work")).FullName,
            Directory.CreateDirectory(Path.Combine(root, "output")).FullName);
    }

    /// <summary>A server that actually works files: one worker, fake tools, and a webhook secret.</summary>
    public static Dictionary<string, string> WorkingEnvironment(FakeFfmpeg tools, params (string Name, string Value)[] extra) =>
        tools.Env.With(
            [
                ("WEIR_PROCESSING_WORKER_COUNT", "1"),
                ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", WebhookSecret),
                .. extra,
            ]);

    /// <summary>A fake Deluno that manages this library, its connection, and the linked library.</summary>
    public static async Task<(FakeManager Fake, JsonObject Library)> DelunoSetupAsync(WeirClient admin, Folders folders)
    {
        var fake = FakeManager.StartDeluno(capabilities: []);
        try
        {
            fake.Libraries.Add(new JsonObject
            {
                ["id"] = DelunoLibraryKey,
                ["name"] = "Movies",
                ["mediaType"] = "movies",
                ["rootPath"] = "/deluno/library/movies",
                ["importWorkflow"] = "refine-before-import",
                ["processorOutputPath"] = DelunoOutputRoot,
            });
            var connection = await CreateConnectionAsync(admin, fake);
            var library = await CreateLibraryAsync(admin, folders, (int)connection["id"]!);
            return (fake, library);
        }
        catch
        {
            fake.Dispose();
            throw;
        }
    }

    public static async Task PostHandoffAsync(
        WeirClient client, string handoffId, string sourcePath, string releaseName = "Contract.Release.2024")
    {
        var response = await client.PostAsync(
            $"{WeirClient.Api}/intake/webhook/deluno",
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
            WebhookHeaders);
        JobsApi.Expect(response, HttpStatusCode.OK);
    }

    public static async Task<JsonObject> WaitForHandoffStateAsync(
        WeirClient client, string handoffId, string state, TimeSpan? timeout = null) =>
        await Poll.UntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"{WeirClient.Api}/intake/handoffs/deluno/{handoffId}", WebhookHeaders);
                JobsApi.Expect(response, HttpStatusCode.OK);
                return (string)response.Fields["state"]! == state ? response.Fields : null;
            },
            $"hand-off {handoffId} to reach '{state}'",
            timeout ?? TimeSpan.FromSeconds(90));

    /// <summary>The inspection list's rows, optionally of one job kind.</summary>
    public static async Task<List<JsonObject>> JobsAsync(WeirClient admin, string? kind = null) =>
        [.. (await JobsApi.InspectionAsync(admin))["jobs"]!.AsArray()
            .Select(row => row!.AsObject())
            .Where(row => kind is null || (string)row["job_kind"]! == kind)];

    private static IReadOnlyDictionary<string, string> WebhookHeaders { get; } =
        new Dictionary<string, string> { ["X-Webhook-Secret"] = WebhookSecret };

    private static async Task<JsonObject> CreateConnectionAsync(WeirClient admin, FakeManager fake)
    {
        var response = await admin.PostWithCsrfAsync(
            $"{WeirClient.Api}/media-managers/connections",
            new JsonObject
            {
                ["kind"] = fake.Kind,
                ["name"] = $"Contract {char.ToUpperInvariant(fake.Kind[0])}{fake.Kind[1..]}",
                ["base_url"] = fake.BaseUrl,
                ["api_key"] = fake.ApiKey,
            });
        Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, response.ToString());
        return response.Fields;
    }

    private static async Task<JsonObject> CreateLibraryAsync(WeirClient admin, Folders folders, int managerConnectionId)
    {
        var response = await admin.PostWithCsrfAsync(
            $"{WeirClient.Api}/processing/libraries",
            new JsonObject
            {
                ["name"] = "Contract Movies",
                ["media_type"] = "movie",
                ["watched_folder"] = folders.Watched,
                ["work_folder"] = folders.Work,
                ["output_folder"] = folders.Output,
                ["ready_after_seconds"] = 0,
                ["min_file_size_mb"] = 0,
                ["skip_access_tests"] = true,
                ["retry_backoff_seconds"] = 1,
                // A workflow keeps 5 GB free by default; a small fixture file is processed whatever the drive holds.
                ["minimum_free_disk_space_mb"] = 0,
                ["manager_connection_ids"] = new JsonArray(managerConnectionId),
            });
        JobsApi.Expect(response, HttpStatusCode.Created);
        return response.Fields;
    }
}
