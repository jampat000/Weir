using System.Net;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Processing;

/// <summary>
/// Behaviour that only shows up against real ffmpeg (#539 items 1 and 3, #494). The fake ffmpeg tool hands a classification
/// straight to Weir (<c>ProbeError</c>), so it never exercises how Weir reads ffprobe's own stderr. Against real ffmpeg:
/// <list type="bullet">
/// <item>On a zero-filled <c>.mkv</c>, ffprobe exits 1, and the classification markers Weir looks for (<c>EBML header parsing
/// failed</c>, etc.) reach stderr only with <c>-v error</c>, not with <c>-v quiet</c>. Weir runs ffprobe with <c>-v error</c>, so the
/// file is classified unreadable and, under the reject policy, the hand-off is reported <c>failed</c> with <c>disposition: rejected</c>.</item>
/// <item>On an MKV truncated after encoding, <c>ffmpeg ... -f null -</c> prints <c>File ended prematurely</c> but still exits 0, and
/// the container header still names the original duration, so an exit-code check alone would accept the file as whole.</item>
/// </list>
/// </summary>
[ContractArea("processing")]
public sealed class RealFfmpegClassifiesDamagedMediaTests
{
    [RealFfmpegFact]
    public async Task Unreadable_zero_filled_media_is_classified_unreadable_and_rejected()
    {
        await using var scenario = await Scenario.StartWithRealToolsAsync();
        // All zero bytes, like the 10GB file from #494 (`fsutil file createnew`) but small enough for a contract test:
        // real ffprobe reports the same "EBML header parsing failed" either way.
        var source = scenario.WriteRelease("Zero.Filled.494", "film.mkv", new byte[2 * 1024 * 1024]);
        var (fake, library) = await scenario.DelunoSetupAsync(
            capabilities: ["processor-reject-regrab"], library: [("failure_policy", "reject")]);

        await scenario.PostHandoffAsync("handoff-zero-494", source);
        await scenario.WaitForHandoffStateAsync("handoff-zero-494", "rejected", TimeSpan.FromSeconds(90));

        var reports = Scenario.Callbacks(fake, "handoff-zero-494");
        Assert.True(reports.Count > 0, "the rejection must be reported to the manager");
        var report = reports[^1];
        Assert.Equal("failed", (string)report["status"]!);
        Assert.Equal("rejected", (string)report["disposition"]!);
        Assert.True(
            string.IsNullOrEmpty((string?)report["outputPath"]),
            "an unreadable file must never be reported completed with a copy of itself as output (#494)");

        // GET /processing/files must not 500 after this (#494 item 3 / #530), and must show the rejection.
        var files = await scenario.Admin.GetAsync($"{WeirClient.Api}/processing/files");
        Assert.True(files.Status == HttpStatusCode.OK, files.ToString());
        var row = await scenario.FileRowAsync(library, "Zero.Filled.494/film.mkv");
        Assert.True(row is not null, "the rejection must leave a Files row behind");
        Assert.Equal("rejected", (string)row["status"]!);
    }

    [RealFfmpegFact]
    public async Task A_truncated_mkv_never_completes_as_if_it_were_whole()
    {
        await using var scenario = await Scenario.StartWithRealToolsAsync();
        var good = Path.Combine(scenario.Root, "good-source.mkv");
        await RealMedia.GenerateAsync(good, seconds: 3, string.Empty);
        var data = await File.ReadAllBytesAsync(good);
        // Cut well short of the end: the container header still names the full duration, so only an
        // end-to-end integrity read can catch this.
        var source = scenario.WriteRelease("Truncated.539", "truncated.mkv", data[..(data.Length * 60 / 100)]);
        await scenario.DelunoSetupAsync();

        await scenario.PostHandoffAsync("handoff-truncated-539", source);

        await Scenario.NeverWithinAsync(
            async () => (string)(await scenario.HandoffStatusAsync("handoff-truncated-539"))["state"]! == "completed",
            TimeSpan.FromSeconds(20),
            "a truncated file to be wrongly reported completed with a false, pre-truncation duration");
    }
}
