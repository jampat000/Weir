using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Processing;

/// <summary>
/// Hand-off retries must not reset the failure count, and must not lose the hand-off's origin when a scan requeues the file.
/// <list type="bullet">
/// <item>A hand-off's file has its size recorded when it arrives. Without that, the first scan after a failure sees a changed size
/// and resets <c>failure_attempts</c> to 0, once. The first test skips <c>DetectWithoutQueueingAsync</c> and checks the fingerprint
/// and the reset directly, because a test that only waited for the final failure count would still reach it after a one-time reset.</item>
/// <item>A scan-driven retry carries the job's <c>origin</c> onto the requeued payload. Without it, the eventual pass-through is
/// reported to nobody: no callback, and <c>GET /intake/handoffs/{source}/{id}</c> keeps <c>outputPath: null</c>.</item>
/// </list>
/// The scan's automatic retry (a processing_failed file due another attempt) is queued like a fresh candidate, not as a person's
/// "retry now", which resets <c>failure_attempts</c> and the backoff and would wipe the count the first test checks on every scan.
/// </summary>
[ContractArea("processing")]
public sealed class HandoffRetryCorrectnessTests
{
    private const string RemuxCrash = "Conversion failed: the fake ffmpeg was told to fail";

    [Fact]
    public async Task A_hand_offs_fingerprint_is_recorded_up_front_so_a_scan_never_resets_its_failures()
    {
        await using var scenario = await Scenario.StartAsync();
        var (_, library) = await scenario.DelunoSetupAsync(
            library: [("failure_policy", "hold"), ("max_attempts", 10), ("retry_backoff_seconds", 1)]);
        scenario.FakeTools.SetFileRule("film.mkv", new FileRule { RemuxError = RemuxCrash });
        var source = scenario.WriteRelease("Always.Broken.531", "film.mkv", FakeMedia.Bytes(FakeMedia.Probe(audioLanguages: ["eng"])));
        const string rel = "Always.Broken.531/film.mkv";

        // No DetectWithoutQueueingAsync: a hand-off's fingerprint must be recorded on arrival, not by a
        // scan run first as a workaround.
        await scenario.PostHandoffAsync("handoff-attempts-531", source);

        var first = await scenario.WaitForFileStatusAsync(library, rel, "processing_failed");
        Assert.Equal(1, (int)first["failure_attempts"]!);
        Assert.True(
            Scenario.Whole(first["size_bytes"]) > 0,
            "a hand-off must record the file's size at intake, not leave it for the first scan to discover");

        // A scan that merely notices this (already-known) file must not treat it as a changed source.
        // enqueueRemuxJobs: false only skips queueing new remux work: the size comparison (and any
        // reset) run regardless, so this isolates the effect of the scan itself.
        await scenario.EnqueueScanAsync(library, enqueueRemuxJobs: false);

        var settled = await Poll.UntilAsync(
            async () =>
            {
                var row = await scenario.FileRowAsync(library, rel);
                return row is not null && !string.IsNullOrEmpty((string?)row["last_seen_at"]) ? row : null;
            },
            "the scan to finish noticing this file",
            TimeSpan.FromSeconds(30));
        Assert.True((string)settled["status"]! == "processing_failed", settled.ToJsonString());
        Assert.True(
            (int)settled["failure_attempts"]! == 1,
            "an intervening scan must not reset the failure count (#531 item 1)");
    }

    [Fact]
    public async Task Pass_through_after_a_retry_reports_a_completion_callback_with_output_path()
    {
        await using var scenario = await Scenario.StartAsync();
        var (fake, library) = await scenario.DelunoSetupAsync(
            library: [("failure_policy", "pass_through"), ("max_attempts", 2), ("retry_backoff_seconds", 1)]);
        scenario.FakeTools.SetFileRule("film.mkv", new FileRule { RemuxError = RemuxCrash });
        var original = FakeMedia.Bytes(FakeMedia.Probe(audioLanguages: ["eng", "fre"]));
        var source = scenario.WriteRelease("Broken.Origin.531", "film.mkv", original);

        // No DetectWithoutQueueingAsync here either: the origin must survive the very first scan-driven
        // retry, so the scenario goes through at least one.
        await scenario.PostHandoffAsync("handoff-origin-531", source);

        await scenario.DriveRetriesUntilAsync(
            library,
            async () => (string)(await scenario.HandoffStatusAsync("handoff-origin-531"))["state"]! == "passed-through",
            "the hand-off to be passed through after a retry");

        var delivered = Path.Combine(scenario.Folders.Output, "Broken.Origin.531", "film.mkv");
        Assert.Equal(original, await File.ReadAllBytesAsync(delivered));

        var status = await scenario.HandoffStatusAsync("handoff-origin-531");
        Assert.False(
            string.IsNullOrEmpty((string?)status["outputPath"]),
            "the origin (and so the output path) must survive a scan-driven retry");
        // The Deluno manifest declares a "refine-before-import" library with its own processorOutputPath, so (same as a
        // first-attempt pass-through, see HandoffScenariosTests) the reported path is rebuilt under the manager's own root,
        // not Weir's local output folder.
        var expected = $"{Scenario.DelunoOutputRoot}/Broken.Origin.531/film.mkv";
        Assert.Equal(expected, ((string)status["outputPath"]!).Replace('\\', '/'));

        var reports = Scenario.Callbacks(fake, "handoff-origin-531");
        Assert.True(reports.Count > 0, "a retried-then-passed-through hand-off must still report a completion callback");
        Assert.False(string.IsNullOrEmpty((string?)reports[^1]["outputPath"]));
    }
}
