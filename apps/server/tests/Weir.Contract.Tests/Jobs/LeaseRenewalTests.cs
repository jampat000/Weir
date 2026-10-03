using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Jobs;

/// <summary>
/// A job that runs longer than its lease must never be claimed by a second worker while the first is still running it.
/// <c>WEIR_PROCESSING_JOB_LEASE_SECONDS</c> shortens the lease (30 s is the shortest the server accepts), and a heartbeat
/// renews it roughly every third of the lease while the handler runs; this proves it over a real hand-off with two workers.
/// </summary>
[ContractArea("jobs")]
public sealed class LeaseRenewalTests
{
    [Fact]
    public async Task A_job_longer_than_its_lease_is_never_claimed_twice()
    {
        // Two workers sharing one database; a remux that runs long enough to outlive a short lease
        // must still only ever be claimed by one of them. 30s is the minimum lease the server accepts;
        // the heartbeat renews it about every 10s, so a 45s remux leaves a comfortable window (past the
        // 5s idle poll) where a second worker would have claimed the row already if renewal were broken.
        using var tools = FakeFfmpeg.Install();
        using var folderRoot = new TemporaryFolder();
        await using var server = await WeirServer.StartNewAsync(JobsHandoff.WorkingEnvironment(
            tools, ("WEIR_PROCESSING_WORKER_COUNT", "2"), ("WEIR_PROCESSING_JOB_LEASE_SECONDS", "30")));
        using var admin = await server.CreateAdminClientAsync();
        var folders = JobsHandoff.Folders.Make(folderRoot.Path);
        using var deluno = (await JobsHandoff.DelunoSetupAsync(admin, folders)).Fake;

        // Deliberately longer than the lease, so a heartbeat is required to keep the lease alive for the
        // whole run; the fake ffmpeg reports its steps as it goes either way.
        tools.SetFileRule("film.mkv", new FileRule { Probe = FakeMedia.Probe(), RemuxDelaySeconds = 45 });
        var release = Directory.CreateDirectory(Path.Combine(folders.Watched, "Long.Running.540")).FullName;
        var source = Path.Combine(release, "film.mkv");
        await File.WriteAllBytesAsync(source, FakeMedia.Bytes(FakeMedia.Probe()));

        await JobsHandoff.PostHandoffAsync(admin, "handoff-lease-540", source);
        await JobsHandoff.WaitForHandoffStateAsync(admin, "handoff-lease-540", "completed", TimeSpan.FromSeconds(120));

        // Only one remux call for this file must ever have started: a second worker claiming the same
        // row mid-write would show up here as two "remux" calls for film.mkv instead of one.
        Assert.Single(await JobsHandoff.JobsAsync(admin, JobsApi.RemuxPassKind));
        Assert.Single(tools.Calls(tool: "ffmpeg", step: "remux"));
    }
}
