using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Activity;

/// <summary>The retention pass: activity and finished jobs older than the horizon are pruned, and a horizon of zero keeps everything.</summary>
[ContractArea("activity")]
public sealed class ActivityRetentionTests
{
    private const string Recent = $"{WeirClient.Api}/activity/recent";
    private const string JobInspection = $"{WeirClient.Api}/processing/jobs/inspection";
    private const string MarkerJobKey = "marker-old-job";

    private static readonly TimeSpan RetentionPassTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Activity_older_than_the_horizon_is_pruned_and_zero_keeps_everything()
    {
        await using var server = await WeirServer.StartNewAsync();
        using (var admin = await server.CreateAdminClientAsync())
        {
            await ActivitySettings.SetActivityRetentionAsync(admin, 0);
        }

        var now = DateTime.UtcNow;
        await using (var database = await server.StopForDatabaseAsync())
        {
            ActivityRows.InsertEvent(database.Connection, "a.old", "processing", "old", createdAt: now - TimeSpan.FromDays(91));
            ActivityRows.InsertEvent(database.Connection, "a.new", "processing", "new", createdAt: now - TimeSpan.FromDays(89));
            // A finished job past the job-row horizon: its removal shows the retention pass has run.
            ActivityRows.InsertCompletedJob(database.Connection, MarkerJobKey, TimeSpan.FromDays(200));
        }

        using var client = server.CreateClient();
        await client.LoginAsync();
        await Poll.UntilAsync(
            async () => !(await CompletedJobKeysAsync(client)).Contains(MarkerJobKey), "the retention pass to run", RetentionPassTimeout);
        Assert.Equal(["new", "old"], await RecentProcessingTitlesAsync(client));

        await ActivitySettings.SetActivityRetentionAsync(client, ActivitySettings.DefaultRetentionDays);
        await using (var database = await server.StopForDatabaseAsync())
        {
            // Job rows default to the same ninety days.
            ActivityRows.InsertCompletedJob(database.Connection, "job-91-days", TimeSpan.FromDays(91));
            ActivityRows.InsertCompletedJob(database.Connection, "job-89-days", TimeSpan.FromDays(89));
        }

        using var restarted = server.CreateClient();
        await restarted.LoginAsync();
        await Poll.UntilAsync(
            async () => (await RecentProcessingTitlesAsync(restarted)).SequenceEqual(["new"]),
            "the old event to be pruned",
            RetentionPassTimeout);
        var keys = await CompletedJobKeysAsync(restarted);
        Assert.DoesNotContain("job-91-days", keys);
        Assert.Contains("job-89-days", keys);
    }

    private static async Task<string[]> RecentProcessingTitlesAsync(WeirClient client)
    {
        var response = await client.GetAsync(Recent, ("module", "processing"), ("limit", 100));
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields["items"]!.AsArray().Select(item => (string)item!["title"]!).Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<HashSet<string>> CompletedJobKeysAsync(WeirClient client)
    {
        var response = await client.GetAsync(JobInspection, ("status", "completed"), ("limit", 100));
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields["jobs"]!.AsArray().Select(job => (string)job!["dedupe_key"]!).ToHashSet();
    }
}
