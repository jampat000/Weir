using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.Jobs;

namespace Weir.Api.Tests;

/// <summary>
/// The environment switches for the Cleanup timers: each one governs its own media type, so switching off Movies leaves TV
/// running and the other way round.
/// </summary>
public sealed class ProcessingEnvironmentSwitchTests
{
    private const string MovieSweep = "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED";
    private const string TvSweep = "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED";
    private const string RetiredMovieCleanup = "WEIR_PROCESSING_MOVIE_FAILURE_CLEANUP_SCHEDULE_ENABLED";
    private const string RetiredTvCleanup = "WEIR_PROCESSING_TV_FAILURE_CLEANUP_SCHEDULE_ENABLED";

    private static async Task<bool> RunsAsync(WeirTestServer server, string timerName)
    {
        var timer = server.Services.GetServices<IPeriodicEnqueuer>().Single(enqueuer => enqueuer.Name == timerName);
        return await timer.IsEnabledAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Switching_off_the_Movies_work_file_sweep_leaves_the_TV_sweep_running()
    {
        await using var server = await WeirTestServer.StartAsync([(MovieSweep, "0")]);

        Assert.False(await RunsAsync(server, "work temp stale sweep (movie)"));
        Assert.True(await RunsAsync(server, "work temp stale sweep (tv)"));
    }

    [Fact]
    public async Task Switching_off_the_TV_work_file_sweep_leaves_the_Movies_sweep_running()
    {
        await using var server = await WeirTestServer.StartAsync([(TvSweep, "0")]);

        Assert.True(await RunsAsync(server, "work temp stale sweep (movie)"));
        Assert.False(await RunsAsync(server, "work temp stale sweep (tv)"));
    }

    [Fact]
    public async Task The_removed_failed_download_cleanup_has_no_timer_whatever_the_environment_says()
    {
        await using var server = await WeirTestServer.StartAsync([(RetiredMovieCleanup, "1"), (RetiredTvCleanup, "1")]);

        var timers = server.Services.GetServices<IPeriodicEnqueuer>().Select(enqueuer => enqueuer.Name).ToList();

        Assert.DoesNotContain(timers, name => name.Contains("failure cleanup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_runtime_settings_do_not_report_a_minimum_file_age_no_pass_reads()
    {
        await using var server = await ApiTestClient.StartServerAsync(("WEIR_PROCESSING_WATCHED_FOLDER_MIN_FILE_AGE_SECONDS", "900"));
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/processing/runtime-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("processing_watched_folder_min_file_age_seconds", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
