using System.Net;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Jobs;

/// <summary>A server with one worker holding a lease on a slow remux, so a test can look at a job that is really leased.</summary>
internal sealed class LeasedJobScenario : IAsyncDisposable
{
    private readonly FakeFfmpeg _tools;
    private readonly TemporaryFolder _folders;
    private readonly WeirServer _server;

    private LeasedJobScenario(FakeFfmpeg tools, TemporaryFolder folders, WeirServer server, WeirClient client, int jobId)
    {
        _tools = tools;
        _folders = folders;
        _server = server;
        Client = client;
        JobId = jobId;
    }

    /// <summary>Signed in as the admin.</summary>
    public WeirClient Client { get; }

    public int JobId { get; }

    public static async Task<LeasedJobScenario> StartAsync()
    {
        var tools = FakeFfmpeg.Install();
        var folders = new TemporaryFolder();
        WeirServer? server = null;
        WeirClient? client = null;
        try
        {
            tools.SetFileRule("*.mkv", new FileRule { RemuxDelaySeconds = 120 });
            server = await WeirServer.StartNewAsync(tools.Env.With(("WEIR_PROCESSING_WORKER_COUNT", "1")));
            client = await server.CreateAdminClientAsync();
            var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "leased-watch")).FullName;
            var output = Directory.CreateDirectory(Path.Combine(folders.Path, "leased-out")).FullName;
            var media = Path.Combine(watched, "film.mkv");
            await File.WriteAllBytesAsync(
                media,
                FakeMedia.Bytes(
                    FakeMedia.Probe(audioLanguages: ["eng", "fre", "ger"], subtitleLanguages: ["eng", "spa"]),
                    padding: 51 * 1024 * 1024)); // above the smallest file Processing will process
            var settled = DateTime.UtcNow.AddSeconds(-7200); // old enough for the settling guardrail
            File.SetLastWriteTimeUtc(media, settled);
            File.SetLastAccessTimeUtc(media, settled);
            var library = await JobsApi.LibraryForScopeAsync(client, "movie");
            await JobsApi.SaveLibraryAsync(client, (int)library["id"]!, ("watched_folder", watched), ("output_folder", output));
            var enqueued = await client.PostWithCsrfAsync(
                $"{WeirClient.Api}/processing/jobs/file-remux-pass/enqueue",
                new System.Text.Json.Nodes.JsonObject { ["relative_media_path"] = "film.mkv" });
            JobsApi.Expect(enqueued, HttpStatusCode.OK);
            var jobId = (int)enqueued.Fields["job_id"]!;
            await Poll.UntilAsync(
                async () => (string)(await JobsApi.JobByIdAsync(client, jobId))["status"]! == "leased",
                "a worker to lease the job",
                TimeSpan.FromSeconds(60));
            return new LeasedJobScenario(tools, folders, server, client, jobId);
        }
        catch
        {
            client?.Dispose();
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            folders.Dispose();
            tools.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _server.DisposeAsync();
        _folders.Dispose();
        _tools.Dispose();
    }
}
