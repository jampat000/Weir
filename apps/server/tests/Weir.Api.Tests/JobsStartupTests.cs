using Microsoft.Extensions.DependencyInjection;
using Weir.Core.Jobs;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Api.Tests;

/// <summary>The jobs area wired into the real server: crash recovery on start, and workers that leave kinds without a handler alone.</summary>
public sealed class JobsStartupTests
{
    [Fact]
    public async Task Every_job_kind_weir_queues_on_a_timer_has_a_handler()
    {
        // A timer job with no handler would wait in the queue for ever.
        await using var server = await WeirTestServer.StartAsync();
        var handled = server.Services.GetServices<IJobHandler>().Select(h => h.JobKind).ToHashSet(StringComparer.Ordinal);

        foreach (var enqueuer in server.Services.GetServices<IPeriodicEnqueuer>())
        {
            Assert.Contains(enqueuer.JobKind, handled);
        }
    }

    [Fact]
    public async Task The_workers_deliver_job_alerts_to_the_channels_on_Settings_Alerts()
    {
        // A notifier that sends nothing would mean no real job ever produces an alert.
        await using var server = await WeirTestServer.StartAsync();

        Assert.IsType<WebhookJobNotifications>(server.Services.GetRequiredService<IJobNotifications>());
    }

    [Fact]
    public async Task Startup_recovers_a_job_a_killed_server_left_leased_and_removes_its_temp_output()
    {
        string? tempFile = null;
        string? operatorFile = null;
        await using var server = await WeirTestServer.StartAsync(
            [("WEIR_PROCESSING_WORKER_COUNT", "1")],
            prepareHome: home =>
            {
                var dbPath = Path.Join(home, "data", "weir.sqlite3");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                var database = new SqliteDatabase(dbPath);
                new SchemaMigrator(database).EnsureAtHead();
                var work = Path.Join(home, "processing", "processing-movie-work");
                Directory.CreateDirectory(work);
                tempFile = Path.Join(work, "film.processing.x1y2z3w4.mkv");
                operatorFile = Path.Join(work, "film.mkv");
                File.WriteAllText(tempFile, "half-written");
                File.WriteAllText(operatorFile, "not Weir's");
                using var connection = database.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, lease_owner, lease_expires_at, attempt_count) " +
                    "VALUES ('crash', 'processing.file.remux_pass.v1', '{\"media_scope\":\"movie\",\"relative_media_path\":\"Crash.Test.2020/film.mkv\"}', " +
                    "'leased', 'killed-host-1-w0', '2999-01-01 00:00:00+00:00', 1)";
                command.ExecuteNonQuery();
                database.ClearPool();
            });

        // Recovery now runs in the background so it never holds up Kestrel from listening (#718).
        var recovery = server.Services.GetRequiredService<JobsStartupRecoveryService>();
        await recovery.RecoveryCompleted.WaitAsync(TimeSpan.FromSeconds(10));
        var report = recovery.LastReport;
        Assert.NotNull(report);
        Assert.Equal(1, report.Jobs.ProcessingRequeued);
        Assert.Equal(1, report.WorkTempFilesRemoved);
        Assert.False(File.Exists(tempFile));
        Assert.True(File.Exists(operatorFile));

        // A running worker handles the remux pass (#522 part 3), so it claims the recovered row and runs it to the end.
        var store = server.Services.GetRequiredService<ProcessingJobStore>();
        var job = await Eventually.PollAsync(
            async () => (await store.ListAsync()).Single(row => row.JobKind == "processing.file.remux_pass.v1"),
            row => row.Status == ProcessingJobStatus.Completed,
            TimeSpan.FromSeconds(30));

        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Null(job.LeaseOwner);
    }
}
