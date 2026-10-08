using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Processing;

/// <summary>
/// A file ffprobe cannot read is not a video Weir can vouch for. Whatever the workflow does with files that need no change, it is refused
/// before any output is written, in plain words: no tool text, memory address or folder path reaches Activity or the media manager.
/// </summary>
[ContractArea("processing")]
public sealed class UnreadableFileTests
{
    private const string UnreadableSentence =
        "Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged. It was refused before any output was written.";

    private const string ProbeFailure =
        "[matroska,webm @ 000001b280590a00] EBML header parsing failed\nC:\\Weir\\Ready\\Movies\\Reject.Film.mkv: Invalid data found when processing input";

    private static byte[] Junk()
    {
        var bytes = new byte[256 * 1024];
        new Random(924).NextBytes(bytes);
        return bytes;
    }

    private static void AssertPlain(string text)
    {
        Assert.DoesNotContain("@ 0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[matroska", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EBML", text, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/", text, StringComparison.Ordinal);
    }

    private static async Task AssertActivityIsPlainAsync(Scenario scenario)
    {
        var recent = await scenario.Admin.GetAsync($"{WeirClient.Api}/activity/recent", ("limit", 100));
        var items = recent.Fields["items"]!.AsArray().OfType<JsonObject>().ToList();
        var failed = items.Where(item => ((string)item["title"]!).Contains("film.mkv", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(failed);
        foreach (var item in failed)
        {
            AssertPlain((string)item["title"]!);
            var detail = JsonNode.Parse((string)item["detail"]!)!.AsObject();
            foreach (var key in new[] { "reason", "preflight_reason", "user_message", "failure_operator_message", "message" })
            {
                if (detail[key] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    AssertPlain(text);
                }
            }
        }
    }

    [Fact]
    public async Task A_junk_file_handed_off_with_pass_through_on_is_refused_before_any_output_and_the_original_stays()
    {
        await using var scenario = await Scenario.StartAsync();
        var (fake, library) = await scenario.DelunoSetupAsync(library: [("failure_policy", "pass_through"), ("max_attempts", 2)]);
        scenario.FakeTools.SetFileRule("film.mkv", new FileRule { ProbeError = ProbeFailure });
        var junk = Junk();
        var source = scenario.WriteRelease("Reject.Film.2013", "film.mkv", junk);

        await scenario.PostHandoffAsync("handoff-junk-1", source);
        await scenario.WaitForHandoffStateAsync("handoff-junk-1", "failed");

        var report = Assert.Single(Scenario.Callbacks(fake, "handoff-junk-1"));
        Assert.Equal("failed", (string)report["status"]!);
        Assert.Equal(UnreadableSentence, (string)report["message"]!);
        Assert.False((bool)report["sourceRemoved"]!);
        Assert.True(string.IsNullOrEmpty((string?)report["outputPath"]));
        AssertPlain(report.ToJsonString());
        // Nothing was written, nothing was handed back, and a linked workflow's original is never touched.
        Assert.Empty(Directory.GetFileSystemEntries(scenario.Folders.Output));
        Assert.Empty(await scenario.JobsAsync(Scenario.PassThroughKind));
        Assert.Empty(scenario.FakeTools.Calls(tool: "ffmpeg", step: "remux"));
        Assert.Equal(junk, await File.ReadAllBytesAsync(source));
        // It was refused, not retried: one attempt, no second.
        Assert.Single(await scenario.JobsAsync(Scenario.RemuxKind));
        var row = await scenario.FileRowAsync(library, "Reject.Film.2013/film.mkv");
        Assert.NotNull(row);
        Assert.Equal("skipped", (string)row["status"]!);
        AssertPlain((string)row["status_reason"]!);
        Assert.Contains(UnreadableSentence, (string)row["status_reason"]!, StringComparison.Ordinal);
        await AssertActivityIsPlainAsync(scenario);
    }

    [Fact]
    public async Task A_junk_file_under_the_reject_policy_is_reported_rejected_in_plain_words()
    {
        await using var scenario = await Scenario.StartAsync();
        var (fake, _) = await scenario.DelunoSetupAsync(capabilities: ["processor-reject-regrab"], library: [("failure_policy", "reject")]);
        scenario.FakeTools.SetFileRule("film.mkv", new FileRule { ProbeError = ProbeFailure });
        var source = scenario.WriteRelease("Reject.Film.2013", "film.mkv", Junk());

        await scenario.PostHandoffAsync("handoff-junk-2", source);
        await scenario.WaitForHandoffStateAsync("handoff-junk-2", "rejected");

        var report = Assert.Single(Scenario.Callbacks(fake, "handoff-junk-2"));
        Assert.Equal("rejected", (string)report["disposition"]!);
        Assert.Equal("preflight", (string)report["failureClass"]!);
        Assert.False((bool)report["sourceRemoved"]!);
        Assert.Contains(UnreadableSentence, (string)report["message"]!, StringComparison.Ordinal);
        AssertPlain(report.ToJsonString());
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFileSystemEntries(scenario.Folders.Output));
        await AssertActivityIsPlainAsync(scenario);
    }

    [Fact]
    public async Task A_readable_file_that_needs_no_change_still_passes_through()
    {
        await using var scenario = await Scenario.StartAsync();
        var library = await scenario.CreateLibraryAsync(("failure_policy", "pass_through"));
        var original = FakeMedia.Bytes(FakeMedia.Probe(audioLanguages: ["eng"]));
        scenario.WriteRelease("Already.Clean.2020", "film.mkv", original);

        await scenario.EnqueueFilePassAsync("Already.Clean.2020/film.mkv", library, passThroughUnchanged: true);

        // A manual pass with no prior scan may leave no Files row, so the job is what is watched.
        await Poll.UntilAsync(
            async () => (await scenario.JobsAsync(Scenario.RemuxKind)).Any(job => (string)job["status"]! == "completed"),
            "the pass to finish");
        var completed = Assert.Single(await scenario.ActivityAsync("processing.file_remux_pass_completed"));
        Assert.Equal("film.mkv was passed through unchanged", (string)completed["title"]!);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(scenario.Folders.Output, "Already.Clean.2020", "film.mkv")));
        Assert.Empty(scenario.FakeTools.Calls(tool: "ffmpeg", step: "remux"));
    }
}
