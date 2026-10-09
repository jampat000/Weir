using System.Text.Json;
using Weir.Core.Media;
using Weir.Core.Rules;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests.Media;

/// <summary>
/// Issue #548 against the real mkvmerge, on tiny files generated at test time. These prove the things a golden
/// argv test cannot: that mkvmerge accepts the argv Weir builds, that what comes out is what the plan asked
/// for, and that it passes #500's output validation — the same validation the ffmpeg writer's output passes,
/// run by the caller so neither writer grades its own work.
/// </summary>
public sealed class MkvmergeRealTests(RealFfmpegFixtures fixtures) : IDisposable, IClassFixture<RealFfmpegFixtures>
{
    private readonly string _root = Directory.CreateTempSubdirectory("weir-real-mkvmerge-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MediaTools Tools() =>
        new(new ProcessRunner(), new RealResolver(), new ListLogger<MediaTools>(), TimeProvider.System);

    private sealed class RealResolver : IMediaToolResolver
    {
        public (string Ffprobe, string Ffmpeg) Resolve() => RealFfmpeg.Tools!.Value;

        public string? ResolveMkvmerge() => RealMkvmerge.Tool;
    }

    private static MkvmergeRemuxWriter Writer(MediaTools tools) => new(tools, new RealResolver());

    private static IReadOnlyList<JsonElement> Streams(JsonElement probe, string codecType) =>
        [.. probe.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == codecType)];

    private static string? Language(JsonElement stream) =>
        stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var language)
            ? language.GetString()
            : null;

    private static RemuxPlan EnglishOnlyPlan(JsonElement probe, MetadataRules? metadata = null)
    {
        var config = RemuxRules.DefaultConfig() with
        {
            PrimaryAudioLang = "eng",
            SecondaryAudioLang = string.Empty,
            TertiaryAudioLang = string.Empty,
            SubtitleMode = RemuxRuleValues.SubtitleModeKeepSelected,
            SubtitleLangs = ["eng"],
            Metadata = metadata ?? new MetadataRules(),
        };
        var split = RemuxRules.SplitStreams(new ProbeResult(probe));
        var plan = RemuxRules.PlanRemux(split.Video, split.Audio, split.Subtitles, config);
        Assert.NotNull(plan);
        return plan;
    }

    // --- identification ----------------------------------------------------------------------

    [RequiresMkvmergeFact]
    public async Task Cover_art_is_an_attachment_to_mkvmerge_and_a_stream_to_ffprobe()
    {
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();

        var probe = await tools.FfprobeJsonAsync(fixture);
        var identification = await tools.IdentifyMkvmergeAsync(RealMkvmerge.Tool!, fixture);

        // ffprobe reports seven streams: the five real tracks, the cover as a second "video" stream flagged
        // attached_pic, and the font as an "attachment" stream. Neither of the last two is a track to mkvmerge.
        Assert.Equal(7, probe.GetProperty("streams").GetArrayLength());
        Assert.Equal(2, Streams(probe, "video").Count);
        Assert.Single(Streams(probe, "attachment"));
        // mkvmerge: 5 tracks and 2 attachments, the cover among the latter.
        Assert.Equal(5, identification.Tracks.Count);
        Assert.Equal(2, identification.Attachments.Count);
        Assert.Contains(identification.Attachments, a => a.IsCoverArt && a.FileName == "cover.jpg");
        Assert.Contains(identification.Attachments, a => !a.IsCoverArt && a.FileName == "TestFont.ttf");
    }

    [RequiresMkvmergeFact]
    public async Task The_track_mapping_skips_the_cover_and_still_lines_up()
    {
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var identification = await tools.IdentifyMkvmergeAsync(RealMkvmerge.Tool!, fixture);
        var streams = probe.GetProperty("streams").EnumerateArray().Select(s => new ProbeStreamInfo(s)).ToList();

        var map = MkvmergeCommands.MapStreamIndicesToTrackIds(streams, identification);

        Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 2, [3] = 3, [4] = 4 }, map);
    }

    // --- writing -----------------------------------------------------------------------------

    [RequiresMkvmergeFact]
    public async Task Mkvmerge_writes_the_plan_and_the_output_passes_the_same_validation_ffmpeg_output_does()
    {
        var fixture = await fixtures.WithAudioAndSubtitleLanguagesAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var plan = EnglishOnlyPlan(probe);
        Assert.Single(plan.Audio);
        Assert.Single(plan.Subtitles);
        var sourceWarnings = await tools.ProbeWarningLinesAsync(fixture);
        var updates = new List<FfmpegProgressUpdate>();

        // ValidateStagedOutputAsync (#500) runs inside this call: it not throwing is half the assertion.
        var staged = await tools.RemuxToTempFileAsync(
            fixture,
            Path.Combine(_root, "work"),
            plan,
            probe,
            sourceWarnings,
            updates.Add,
            ProbeOutput.DurationSeconds(probe),
            writer: Writer(tools));
        var output = staged.Path;

        var outputProbe = await tools.FfprobeJsonAsync(output);
        Assert.Equal("eng", Language(Assert.Single(Streams(outputProbe, "audio"))));
        Assert.Equal("eng", Language(Assert.Single(Streams(outputProbe, "subtitle"))));
        Assert.Single(Streams(outputProbe, "video"));
        Assert.NotEmpty(updates);
        Assert.Equal(100.0, updates[^1].Percent);
        Assert.Equal("end", updates[^1].Progress);
    }

    [RequiresMkvmergeFact]
    public async Task Kept_cover_art_hands_the_write_back_to_ffmpeg()
    {
        // The default: RemoveImages is off, so the planner keeps the cover in VideoIndices as a video stream to
        // map. mkvmerge would write it as the attachment it really is, which #500's positional validation reads
        // as the wrong shape — so ffmpeg writes this one and the output is exactly today's.
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var plan = EnglishOnlyPlan(probe);
        Assert.Equal(2, plan.VideoIndices.Count);

        var staged = await tools.RemuxToTempFileAsync(
            fixture,
            Path.Combine(_root, "work-attached"),
            plan,
            probe,
            await tools.ProbeWarningLinesAsync(fixture),
            writer: Writer(tools));
        var output = staged.Path;

        Assert.IsType<FfmpegRemuxWriter>(staged.Writer);
        // ffmpeg's shape: the cover is an output video stream, and #547's attachment map kept the font.
        var outputProbe = await tools.FfprobeJsonAsync(output);
        Assert.Equal(2, Streams(outputProbe, "video").Count);
        Assert.Single(Streams(outputProbe, "attachment"));
    }

    [RequiresMkvmergeFact]
    public async Task The_refusal_is_specific_so_only_this_case_falls_back()
    {
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var plan = EnglishOnlyPlan(probe);

        var error = await Assert.ThrowsAsync<MkvmergeUnsupportedPlanException>(
            () => Writer(tools).WriteAsync(new RemuxWriteRequest(fixture, Path.Combine(_root, "direct.mkv"), plan, probe)));

        Assert.Contains("cover art", error.Message, StringComparison.Ordinal);
    }

    [RequiresMkvmergeFact]
    public async Task Removing_images_lets_mkvmerge_write_it_and_drops_the_cover()
    {
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var plan = EnglishOnlyPlan(probe, new MetadataRules { RemoveImages = true });
        // With the images dropped the plan keeps only the real video track, which mkvmerge can address.
        Assert.Single(plan.VideoIndices);

        var staged = await tools.RemuxToTempFileAsync(
            fixture,
            Path.Combine(_root, "work-no-images"),
            plan,
            probe,
            await tools.ProbeWarningLinesAsync(fixture),
            writer: Writer(tools));
        var output = staged.Path;

        var identification = await tools.IdentifyMkvmergeAsync(RealMkvmerge.Tool!, output);
        var kept = Assert.Single(identification.Attachments);
        Assert.Equal("TestFont.ttf", kept.FileName);
    }

    [RequiresMkvmergeFact]
    public async Task Removing_images_and_attachments_leaves_neither()
    {
        var fixture = await fixtures.WithMkvmergeAttachmentsAsync();
        var tools = Tools();
        var probe = await tools.FfprobeJsonAsync(fixture);
        var plan = EnglishOnlyPlan(probe, new MetadataRules { RemoveImages = true, RemoveAttachments = true });

        var staged = await tools.RemuxToTempFileAsync(
            fixture,
            Path.Combine(_root, "work-bare"),
            plan,
            probe,
            await tools.ProbeWarningLinesAsync(fixture),
            writer: Writer(tools));
        var output = staged.Path;

        var identification = await tools.IdentifyMkvmergeAsync(RealMkvmerge.Tool!, output);
        Assert.Empty(identification.Attachments);
    }

    // --- writer selection ---------------------------------------------------------------------

    [RequiresMkvmergeFact]
    public void Mkvmerge_takes_matroska_and_leaves_every_other_container_to_ffmpeg()
    {
        var writer = Writer(Tools());

        Assert.True(writer.CanWrite("out.mkv"));
        Assert.False(writer.CanWrite("out.mp4"));
        // WebM stays with ffmpeg: see the #503 trial's recommendation.
        Assert.False(writer.CanWrite("out.webm"));
    }
}
