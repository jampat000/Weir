using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Api.Tests;

/// <summary>
/// The environment switches for the Cleanup timers: each one governs its own media type, so switching off Movies leaves TV
/// running and the other way round.
/// </summary>
public sealed class ProcessingEnvironmentSwitchTests
{
    private const string MovieSweep = "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED";
    private const string TvSweep = "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED";
    private const string MovieCleanup = "WEIR_PROCESSING_MOVIE_FAILURE_CLEANUP_SCHEDULE_ENABLED";
    private const string TvCleanup = "WEIR_PROCESSING_TV_FAILURE_CLEANUP_SCHEDULE_ENABLED";

    /// <summary>A home whose saved Cleanup settings switch the failure cleanup on, so only the environment can stop it.</summary>
    private static void SaveFailureCleanupOn(string home)
    {
        var databasePath = Path.Join(home, "data", "weir.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new SqliteDatabase(databasePath);
        new SchemaMigrator(database).EnsureAtHead();
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE operator_settings SET failure_cleanup_enabled = 1";
            command.ExecuteNonQuery();
        }

        database.ClearPool();
    }

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
    public async Task Switching_off_the_Movies_failure_cleanup_leaves_the_TV_cleanup_running()
    {
        await using var server = await WeirTestServer.StartAsync([(MovieCleanup, "0")], prepareHome: SaveFailureCleanupOn);

        Assert.False(await RunsAsync(server, "failure cleanup sweep (movie)"));
        Assert.True(await RunsAsync(server, "failure cleanup sweep (tv)"));
    }

    [Fact]
    public async Task Switching_off_the_TV_failure_cleanup_leaves_the_Movies_cleanup_running()
    {
        await using var server = await WeirTestServer.StartAsync([(TvCleanup, "0")], prepareHome: SaveFailureCleanupOn);

        Assert.True(await RunsAsync(server, "failure cleanup sweep (movie)"));
        Assert.False(await RunsAsync(server, "failure cleanup sweep (tv)"));
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
