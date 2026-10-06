using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Processing runtime settings: who may read them, and their shape.</summary>
[ContractArea("libraries")]
public sealed class ProcessingRuntimeSettingsApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string SettingsPath = Api + "/processing/runtime-settings";

    private static readonly string[] ReportedKeys =
    [
        "worker_mode_summary",
        "sqlite_throughput_note",
        "configuration_note",
        "visibility_note",
        "processing_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs",
        "processing_probe_size_mb",
        "processing_analyze_duration_seconds",
        "processing_movie_output_cleanup_min_age_seconds",
        "movie_output_cleanup_configuration_note",
        "processing_tv_output_cleanup_min_age_seconds",
        "tv_output_cleanup_configuration_note",
        "processing_work_temp_stale_sweep_movie_schedule_enabled",
        "processing_work_temp_stale_sweep_movie_schedule_interval_seconds",
        "processing_work_temp_stale_sweep_tv_schedule_enabled",
        "processing_work_temp_stale_sweep_tv_schedule_interval_seconds",
        "processing_work_temp_stale_sweep_min_stale_age_seconds",
        "work_temp_stale_sweep_periodic_configuration_note",
        "watched_folder_scan_periodic_configuration_note",
    ];

    [Fact]
    public async Task Processing_runtime_settings_requires_auth()
    {
        using var client = fixture.Server.CreateClient();

        (await client.GetAsync(SettingsPath)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Processing_runtime_settings_operator_shape()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(SettingsPath);

        response.ShouldBe(HttpStatusCode.OK);
        var body = response.Fields;
        Assert.Equal(0, (int)body["in_process_processing_worker_count"]!);
        Assert.True((bool)body["in_process_workers_disabled"]!);
        Assert.False((bool)body["in_process_workers_enabled"]!);
        foreach (var key in ReportedKeys)
        {
            Assert.True(body.ContainsKey(key), key);
        }

        // Not reported (#329): the scheduler reads the per-scope database toggles, so these would
        // misdescribe the live configuration.
        Assert.DoesNotContain(body, pair => pair.Key.Contains("failure_cleanup", StringComparison.Ordinal));
        Assert.False(body.ContainsKey("processing_watched_folder_remux_scan_dispatch_schedule_enabled"));
        Assert.False(body.ContainsKey("processing_watched_folder_remux_scan_dispatch_schedule_interval_seconds"));
    }

    [Fact]
    public async Task Processing_runtime_settings_viewer_forbidden()
    {
        await SeededAccounts.EnsureViewerAsync(fixture.Server);
        using var viewer = await SeededAccounts.SignInViewerAsync(fixture.Server);

        var response = await viewer.GetAsync(SettingsPath);

        response.ShouldBe(HttpStatusCode.Forbidden);
    }
}
