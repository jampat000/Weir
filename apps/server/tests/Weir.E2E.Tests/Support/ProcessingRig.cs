using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using Weir.E2E.Tests.Harness;

namespace Weir.E2E.Tests.Support;

/// <summary>
/// A Weir of its own that really processes files, for a test that watches a file move through the screens. It runs one worker
/// against a fake ffmpeg, so a pass stays under way until the test releases it, and a fake Deluno takes the finished
/// hand-off's report. Files arrive and finish the way a media manager makes them: a hand-off on the webhook, then the
/// manager's word on what it did with the copy. Nothing is written to the database.
/// </summary>
public sealed class ProcessingRig : IAsyncDisposable
{
    private const string WebhookSecret = "e2e-rig-webhook-secret";
    private const string WebhookSecretVariable = "WEIR_MEDIA_MANAGER_WEBHOOK_SECRET";
    private const string CallbackPath = "/api/integrations/processors/events";

    private static readonly IReadOnlyDictionary<string, string> Secret = new Dictionary<string, string> { ["X-Webhook-Secret"] = WebhookSecret };

    private readonly FakeFfmpeg _tools;
    private readonly WeirServer _server;
    private readonly WeirClient _admin;
    private readonly FakeManager _deluno;
    private readonly TemporaryFolder _folders;

    private ProcessingRig(FakeFfmpeg tools, WeirServer server, WeirClient admin, FakeManager deluno, TemporaryFolder folders)
    {
        _tools = tools;
        _server = server;
        _admin = admin;
        _deluno = deluno;
        _folders = folders;
    }

    /// <summary>The server's address without a trailing slash.</summary>
    public string BaseUrl => _server.BaseUrl.GetLeftPart(UriPartial.Authority);

    private string Watched => Path.Combine(_folders.Path, "watched");

    public static async Task<ProcessingRig> StartAsync()
    {
        var tools = FakeFfmpeg.Install();
        var folders = new TemporaryFolder();
        var deluno = FakeManager.StartDeluno();
        WeirServer? server = null;
        WeirClient? admin = null;
        try
        {
            server = await WeirServer.StartNewAsync(new Dictionary<string, string>(tools.Env)
            {
                ["WEIR_WEB_DIST"] = RepoPaths.WebDist,
                ["WEIR_PROCESSING_WORKER_COUNT"] = "1",
                [WebhookSecretVariable] = WebhookSecret,
            });
            admin = new WeirClient(server.BaseUrl);
            await admin.EnsureAdminAsync(Navigation.BootstrapUser, Navigation.BootstrapPassword);
            await PrepareMoviesWorkflowAsync(admin, folders.Path);
            var connection = await admin.PostWithCsrfAsync($"{WeirClient.Api}/media-managers/connections", new JsonObject
            {
                ["kind"] = "deluno",
                ["base_url"] = deluno.BaseUrl,
                ["api_key"] = deluno.ApiKey,
                ["enabled"] = true,
            });
            Assert.True(connection.Status == HttpStatusCode.Created, connection.ToString());
            return new ProcessingRig(tools, server, admin, deluno, folders);
        }
        catch
        {
            admin?.Dispose();
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            deluno.Dispose();
            tools.Dispose();
            folders.Dispose();
            throw;
        }
    }

    /// <summary>Points the seeded Movies workflow at folders that exist, with no wait and no minimum size.</summary>
    private static async Task PrepareMoviesWorkflowAsync(WeirClient admin, string root)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        var watched = Directory.CreateDirectory(Path.Combine(root, "watched")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        var libraries = $"{WeirClient.Api}/processing/libraries";
        var listed = await admin.GetAsync(libraries);
        Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
        var movies = listed.Elements.Select(row => row!.AsObject()).First(row => (string?)row["media_type"] == "movie");
        var body = LibraryBodies.Unchanged(movies);
        body["watched_folder"] = watched;
        body["output_folder"] = output;
        body["work_folder"] = work;
        body["ready_after_seconds"] = 0;
        body["skip_access_tests"] = true;
        body["min_file_size_mb"] = 0;
        var saved = await admin.PutWithCsrfAsync($"{libraries}/{(long)movies["id"]!}", body);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
        var settings = await admin.PutWithCsrfAsync($"{WeirClient.Api}/processing/operator-settings", new JsonObject { ["minimum_free_disk_space_mb"] = 0 });
        Assert.True(settings.Status == HttpStatusCode.OK, settings.ToString());
    }

    /// <summary>
    /// Deluno hands over a movie. Its pass stays under way, once a worker takes it, until <see cref="ReleasePass"/>. The hand-off
    /// is answered by the server before the file moves anywhere on screen.
    /// </summary>
    public async Task HandOffAsync(string handoffId, string fileName)
    {
        var source = Path.Combine(Watched, handoffId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, FakeMedia.Bytes(FakeMedia.Probe()));
        _tools.SetFileRule(fileName, new FileRule { Probe = FakeMedia.Probe(), RemuxReleaseFile = ReleaseFileFor(fileName) });
        using var deluno = _server.CreateClient();
        var response = await deluno.PostAsync($"{WeirClient.Api}/intake/webhook/deluno", new JsonObject
        {
            ["eventType"] = "deluno.processor-handoff",
            ["handoffId"] = handoffId,
            ["libraryId"] = "lib-1",
            ["mediaType"] = "movies",
            ["sourcePath"] = source,
            ["callbackPath"] = CallbackPath,
        }, Secret);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
    }

    /// <summary>Lets the pass on <paramref name="fileName"/> finish.</summary>
    public void ReleasePass(string fileName) => File.WriteAllText(ReleaseFileFor(fileName), string.Empty);

    /// <summary>Waits until Weir has handed the finished file back to Deluno and told it so.</summary>
    public async Task WaitForReportAsync() =>
        await _deluno.WaitForRequestAsync("POST", CallbackPath);

    /// <summary>Deluno says what it did with the copy Weir handed back, as it does once it has imported it.</summary>
    public async Task ReportImportedAsync(string handoffId)
    {
        using var deluno = _server.CreateClient();
        var response = await deluno.PostAsync(
            $"{WeirClient.Api}/intake/handoffs/deluno/{handoffId}/outcome",
            new JsonObject { ["outcome"] = "imported", ["occurredUtc"] = DateTimeOffset.UtcNow.ToString("O") },
            Secret);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
    }

    private string ReleaseFileFor(string fileName) => Path.Combine(_folders.Path, $"release-{fileName}");

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _server.DisposeAsync();
        _deluno.Dispose();
        _tools.Dispose();
        _folders.Dispose();
    }
}
