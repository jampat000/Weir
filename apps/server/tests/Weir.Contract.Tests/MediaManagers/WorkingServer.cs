using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// A server with one real worker and the fake tools, a Movies library that takes a fresh file at once, a fake Deluno connected to
/// it, and one media file waiting in the watched folder.
/// </summary>
internal sealed class WorkingServer : IAsyncDisposable
{
    private WorkingServer(WeirServer server, WeirClient admin, LibraryFolders folders, FakeManager deluno)
    {
        Server = server;
        Admin = admin;
        Folders = folders;
        Deluno = deluno;
    }

    public WeirServer Server { get; }

    public WeirClient Admin { get; }

    public LibraryFolders Folders { get; }

    public FakeManager Deluno { get; }

    public string Source => Path.Combine(Folders.Watched, "Film", "film.mkv");

    /// <summary>Starts the server; the caller disposes <paramref name="tools"/> and deletes <paramref name="root"/> after this server is gone.</summary>
    public static async Task<WorkingServer> StartAsync(FakeFfmpeg tools, string root)
    {
        var deluno = FakeManager.StartDeluno();
        WeirServer? server = null;
        WeirClient? admin = null;
        try
        {
            server = await WeirServer.StartNewAsync(
                Handoffs.SecretEnvironment.With(tools.Env).With(("WEIR_PROCESSING_WORKER_COUNT", "1")));
            admin = await server.CreateAdminClientAsync();
            var folders = LibraryFolders.Make(Path.Combine(root, "library"));
            await PrepareMoviesLibraryAsync(admin, folders, Path.Combine(root, "library", "work"));
            var connection = await ManagerConnections.CreateAsync(admin, new JsonObject
            {
                ["name"] = "Deluno",
                ["base_url"] = deluno.BaseUrl,
                ["api_key"] = deluno.ApiKey,
            });
            Assert.True(connection.Status == HttpStatusCode.Created, connection.ToString());
            var working = new WorkingServer(server, admin, folders, deluno);
            Directory.CreateDirectory(Path.GetDirectoryName(working.Source)!);
            await File.WriteAllBytesAsync(working.Source, FakeMedia.Bytes(FakeMedia.Probe()));
            return working;
        }
        catch
        {
            admin?.Dispose();
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            deluno.Dispose();
            throw;
        }
    }

    public Task<string?> JobStatusAsync(string handoffId) => RemuxJobs.StatusForHandoffAsync(Admin, handoffId);

    public async Task WaitUntilLeasedAsync(string handoffId) =>
        await Poll.UntilAsync(async () => await JobStatusAsync(handoffId) == "leased", "the worker to take the job", TimeSpan.FromSeconds(60));

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        await Server.DisposeAsync();
        Deluno.Dispose();
    }

    private static async Task PrepareMoviesLibraryAsync(WeirClient admin, LibraryFolders folders, string work)
    {
        Directory.CreateDirectory(work);
        var changes = new JsonObject
        {
            ["watched_folder"] = folders.Watched,
            ["output_folder"] = folders.Output,
            ["work_folder"] = work,
            ["ready_after_seconds"] = 0,
            ["skip_access_tests"] = true,
            ["min_file_size_mb"] = 0,
            ["minimum_free_disk_space_mb"] = 0,
        };
        var seeded = (await ProcessingLibraries.ListAsync(admin)).FirstOrDefault(row => (string?)row!["media_type"] == "movie");
        if (seeded is null)
        {
            await ProcessingLibraries.CreateAsync(admin, "Movies", "movie", folders, changes);
        }
        else
        {
            await ProcessingLibraries.UpdateAsync(admin, seeded.AsObject(), changes);
        }
    }
}
