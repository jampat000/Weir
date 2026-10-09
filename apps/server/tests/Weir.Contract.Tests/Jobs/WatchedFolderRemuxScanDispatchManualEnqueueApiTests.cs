using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>Manually queueing a watched-folder scan: it needs a watched folder, and by default it queues remux jobs for what it finds.</summary>
[ContractArea("jobs")]
public sealed class WatchedFolderRemuxScanDispatchManualEnqueueApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Enqueue = $"{WeirClient.Api}/processing/jobs/watched-folder-remux-scan-dispatch/enqueue";

    [Fact]
    public async Task Watched_folder_scan_enqueue_requires_watched_folder()
    {
        using var folders = new TemporaryFolder();
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "out_scan_api")).FullName;
        using var admin = await fixture.Server.CreateAdminClientAsync();
        await JobsApi.SetMovieFoldersAsync(admin, watched: null, output);

        var response = await admin.PostWithCsrfAsync(Enqueue, new JsonObject());

        JobsApi.Expect(response, HttpStatusCode.BadRequest);
        Assert.Contains("watched folder", ((string)response.Fields["detail"]!).ToLowerInvariant());
    }

    [Fact]
    public async Task Scan_now_on_a_watched_folder_that_is_gone_says_so_where_the_person_clicked()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "w_scan_gone")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "out_scan_gone")).FullName;
        using var admin = await fixture.Server.CreateAdminClientAsync();
        await JobsApi.SetMovieFoldersAsync(admin, watched, output);
        Directory.Delete(watched);

        var response = await admin.PostWithCsrfAsync(Enqueue, new JsonObject());

        JobsApi.Expect(response, HttpStatusCode.BadRequest);
        var detail = (string)response.Fields["detail"]!;
        Assert.Contains("can't see the watched folder", detail, StringComparison.Ordinal);
        Assert.Contains(watched, detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watched_folder_scan_enqueue_ok()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "w_scan_api")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "out_scan_api2")).FullName;
        using var admin = await fixture.Server.CreateAdminClientAsync();
        await JobsApi.SetMovieFoldersAsync(admin, watched, output);

        var response = await admin.PostWithCsrfAsync(Enqueue, new JsonObject { ["enqueue_remux_jobs"] = false });

        JobsApi.Expect(response, HttpStatusCode.OK);
        Assert.Equal(JobsApi.ScanDispatchKind, (string)response.Fields["job_kind"]!);
    }

    [Fact]
    public async Task Watched_folder_scan_enqueue_defaults_to_processing_files()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "w_scan_api_default")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "out_scan_api_default")).FullName;
        using var admin = await fixture.Server.CreateAdminClientAsync();
        await JobsApi.SetMovieFoldersAsync(admin, watched, output);

        var response = await admin.PostWithCsrfAsync(Enqueue, new JsonObject());

        JobsApi.Expect(response, HttpStatusCode.OK);
        var job = await JobsApi.JobByIdAsync(admin, (int)response.Fields["job_id"]!);
        Assert.Contains("\"enqueue_remux_jobs\":true", (string?)job["payload_json"] ?? "");
    }
}
