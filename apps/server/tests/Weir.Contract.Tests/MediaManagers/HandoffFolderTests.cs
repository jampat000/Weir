using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// Folder hand-offs for media managers: Deluno names the completed download's folder, not a file. Auth and status are in
/// <see cref="HandoffStatusTests"/>; running work, cancelling and retention are in <see cref="HandoffLifecycleTests"/>.
/// </summary>
[ContractArea("media_managers")]
public sealed class HandoffFolderTests(HandoffServerFixture fixture) : IClassFixture<HandoffServerFixture>
{
    private WeirServer Server => fixture.Server;

    private static async Task<LibraryFolders> FolderLibraryAsync(WeirClient admin, string root)
    {
        var folders = LibraryFolders.Make(root);
        await ProcessingLibraries.CreateAsync(admin, $"Folder {Guid.NewGuid().ToString("N")[..8]}", "movie", folders);
        return folders;
    }

    [Fact]
    public async Task A_folder_hand_off_queues_the_video_inside_it_not_the_folder_or_the_sample()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var temp = new TemporaryFolder();
        var watched = (await FolderLibraryAsync(admin, Path.Combine(temp.Path, "folder-library"))).Watched;
        var release = Path.Combine(watched, "Blade.Runner.2049");
        Directory.CreateDirectory(Path.Combine(release, "Sample"));
        await File.WriteAllBytesAsync(Path.Combine(release, "Blade.Runner.2049.mkv"), "x"u8.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(release, "Sample", "sample.mkv"), "x"u8.ToArray());
        await File.WriteAllTextAsync(Path.Combine(release, "movie.nfo"), "x");
        var handoffId = Handoffs.NewId();
        var key = RemuxJobs.HandoffDedupeKey(handoffId);

        async Task<List<JsonObject>> JobsAsync() => [.. (await RemuxJobs.ListAsync(admin))
            .Where(job => (string?)job["dedupe_key"] is { } dedupe && (dedupe == key || dedupe.StartsWith($"{key}:", StringComparison.Ordinal)))];

        Assert.Equal(HttpStatusCode.OK, (await Handoffs.HandOffResponseAsync(Server, handoffId, release)).Status);
        var job = Assert.Single(await JobsAsync());
        Assert.Contains("\"relative_media_path\":\"Blade.Runner.2049/Blade.Runner.2049.mkv\"", (string?)job["payload_json"] ?? string.Empty);
        Assert.Equal("Blade.Runner.2049/Blade.Runner.2049.mkv", (string)RemuxJobs.Payload(job)["relative_media_path"]!);
        // A repeated hand-off returns the same job rather than queueing the file twice.
        Assert.Equal(HttpStatusCode.OK, (await Handoffs.HandOffResponseAsync(Server, handoffId, release)).Status);
        Assert.Single(await JobsAsync());
    }

    [Fact]
    public async Task A_folder_with_no_video_is_refused_with_a_reason()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var temp = new TemporaryFolder();
        var folders = await FolderLibraryAsync(admin, Path.Combine(temp.Path, "folder-library"));
        var release = Path.Combine(folders.Watched, "Empty.Release");
        Directory.CreateDirectory(release);
        await File.WriteAllTextAsync(Path.Combine(release, "readme.txt"), "x");

        var response = await Handoffs.HandOffResponseAsync(Server, Handoffs.NewId(), release);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("no video file", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task A_folder_hand_off_answers_for_the_files_inside_it()
    {
        using var temp = new TemporaryFolder();
        LibraryFolders folders;
        using (var admin = await Server.CreateAdminClientAsync())
        {
            folders = await FolderLibraryAsync(admin, Path.Combine(temp.Path, "folder-library"));
        }

        var release = Path.Combine(folders.Watched, "Film");
        Directory.CreateDirectory(release);
        await File.WriteAllBytesAsync(Path.Combine(release, "film.mkv"), "x"u8.ToArray());
        var handoffId = Handoffs.NewId();
        Assert.Equal(HttpStatusCode.OK, (await Handoffs.HandOffResponseAsync(Server, handoffId, release)).Status);
        await Handoffs.SeedAsync(Server, handoffId, jobStatus: "completed", fileStatus: "processed", relativePath: "Film/film.mkv");

        Assert.Equal("completed", (string)(await Handoffs.StatusAsync(Server, handoffId))["state"]!);
    }

    [Fact]
    public async Task A_file_weir_gave_up_on_is_failed_not_queued()
    {
        var movies = await fixture.MoviesAsync();
        var handoffId = Handoffs.NewId();
        await Handoffs.HandOffAsync(Server, handoffId, Path.Combine(movies.Watched, handoffId, "film.mkv"));
        await Handoffs.SeedAsync(
            Server, handoffId, jobStatus: "completed", fileStatus: "processing_failed", file: new FileColumns(FailureAttempts: 3));

        Assert.Equal("failed", (string)(await Handoffs.StatusAsync(Server, handoffId))["state"]!);
    }
}
