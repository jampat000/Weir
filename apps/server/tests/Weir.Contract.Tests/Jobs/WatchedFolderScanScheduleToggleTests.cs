using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Jobs;

/// <summary>
/// The switch that turns off periodic watched-folder scans (<c>WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED</c>)
/// must stop the scan timer, while a manual scan still works. The switched-off case is read from the library's
/// <c>periodic_scan</c> and <c>next_scan_at</c> fields; a positive control with the switch on shows the timer does run,
/// so a quiet timer cannot pass for a working switch.
/// </summary>
[ContractArea("jobs")]
public sealed class WatchedFolderScanScheduleToggleTests
{
    private const string Enqueue = $"{WeirClient.Api}/processing/jobs/watched-folder-remux-scan-dispatch/enqueue";

    // The scheduler clamps every library's cadence to at least 10s.
    private const int ShortIntervalSeconds = 10;

    private static async Task<JsonObject> ConfigureShortScanAsync(WeirClient admin, string watched, string output)
    {
        var movie = await JobsApi.LibraryForScopeAsync(admin, "movie");
        return await JobsApi.SaveLibraryAsync(
            admin,
            (int)movie["id"]!,
            ("watched_folder", watched),
            ("output_folder", output),
            ("scan_interval_seconds", ShortIntervalSeconds),
            ("skip_access_tests", true));
    }

    private static async Task<int> ScanJobCountAsync(WeirClient admin)
    {
        var inspection = await JobsApi.InspectionAsync(admin, 100);
        return inspection["jobs"]!.AsArray().Count(job => (string)job!["job_kind"]! == JobsApi.ScanDispatchKind);
    }

    [Fact]
    public async Task Disabling_the_periodic_scan_switch_reports_it_off()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "watched")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "output")).FullName;
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string>
        {
            ["WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED"] = "0",
        });
        using var admin = await server.CreateAdminClientAsync();

        var saved = await ConfigureShortScanAsync(admin, watched, output);

        Assert.Equal("off", (string)saved["periodic_scan"]!);
        Assert.True(saved.ContainsKey("next_scan_at"));
        Assert.Null(saved["next_scan_at"]);

        // A manual scan must still work regardless of the periodic switch.
        var manual = await admin.PostWithCsrfAsync(Enqueue, new JsonObject { ["enqueue_remux_jobs"] = false });
        JobsApi.Expect(manual, HttpStatusCode.OK);
        var job = await JobsApi.JobByIdAsync(admin, (int)manual.Fields["job_id"]!);
        Assert.Equal(JobsApi.ScanDispatchKind, (string)job["job_kind"]!);
    }

    /// <summary>
    /// Positive control for the test above: with the switch untouched (on by default), the same library on the same
    /// short cadence must eventually get a periodic scan, proving the negative result above is the switch working
    /// and not the timer coincidentally never firing in time.
    /// </summary>
    [Fact]
    public async Task Periodic_scan_switch_left_on_lets_the_scan_timer_run()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Combine(folders.Path, "watched")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(folders.Path, "output")).FullName;
        await using var server = await WeirServer.StartNewAsync();
        using (var configuring = await server.CreateAdminClientAsync())
        {
            var saved = await ConfigureShortScanAsync(configuring, watched, output);
            Assert.Equal("scheduled", (string)saved["periodic_scan"]!);
        }

        // Restart so the scheduler's first tick sees the library already configured, rather than racing
        // this test's own setup calls and locking in a stale "not ready yet" due time for a full interval
        // (the scheduler can queue its first attempt almost as soon as the process is up).
        await server.RestartAsync();
        using var admin = await server.CreateAdminClientAsync();

        // No manual scan preceded this, so any scan-dispatch job at all here is the periodic timer's doing;
        // it can fire before this line even runs, so this checks for one rather than a delta.
        await Poll.UntilAsync(
            async () => await ScanJobCountAsync(admin) > 0,
            "a periodic scan-dispatch job with the switch on",
            TimeSpan.FromSeconds(ShortIntervalSeconds + 20));
    }
}
