using System.Collections.Concurrent;
using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests.Media;

/// <summary>
/// The small media files the real-ffmpeg tests read, made with <c>-f lavfi</c>. Each is generated the first time a test
/// asks for it and kept for the rest of the run, so a run pays for each file once however many tests share it. The tests
/// only read these; what a test writes goes in its own folder.
/// </summary>
public sealed class RealFfmpegFixtures : IDisposable
{
    /// <summary>
    /// A fixture is three seconds of tiny video, so this is only reached when the machine is starved, and then it names the
    /// cause instead of failing the test on a missing file.
    /// </summary>
    private static readonly TimeSpan GenerationLimit = TimeSpan.FromMinutes(5);

    private readonly string _root = Directory.CreateTempSubdirectory("weir-real-ffmpeg-fixtures-").FullName;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _generated = new(StringComparer.Ordinal);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Runs ffmpeg (or another tool) for a test, failing with a message that says what was being made and why it stopped.</summary>
    internal static async Task RunAsync(string what, IReadOnlyList<string> argv)
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRequest { Argv = argv, Timeout = GenerationLimit }).ConfigureAwait(false);
        Assert.False(result.TimedOut, $"{what} did not finish within {GenerationLimit.TotalMinutes:0} minutes; the machine is probably overloaded.");
        Assert.True(result.ExitCode == 0, $"{what} failed: " + ProbeOutput.TailText(result.Stderr));
    }

    /// <summary>ffmpeg with the options every fixture and test command starts with.</summary>
    internal static string[] Ffmpeg(params string[] arguments) =>
        [RealFfmpeg.Tools!.Value.Ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", .. arguments];

    /// <summary>Three seconds: mpeg4 video, English and French AAC tracks.</summary>
    public Task<string> StandardAsync() => GenerateAsync("fixture.mkv", StandardArguments);

    /// <summary>The standard file cut to one second.</summary>
    public Task<string> ShortAsync() => GenerateAsync("short.mkv", path => [.. StandardArguments(path)[..^1], "-t", "1", path]);

    /// <summary>The standard file as MP4 with its index first, so only the media data can be cut off.</summary>
    public Task<string> Mp4Async() => GenerateAsync("fixture.mp4", path => [.. StandardArguments(path)[..^1], "-movflags", "+faststart", path]);

    /// <summary>Three seconds: mpeg4 video, an English AAC track and an English SRT subtitle track.</summary>
    public Task<string> WithSubtitleAsync() => GenerateAsync(
        "with-subs.mkv",
        path =>
        {
            var subtitlePath = Path.ChangeExtension(path, ".srt");
            File.WriteAllText(subtitlePath, "1\n00:00:00,000 --> 00:00:03,000\nHello\n");
            return Ffmpeg(
                "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                "-i", subtitlePath,
                "-map", "0", "-map", "1", "-map", "2",
                "-c:v", "mpeg4", "-c:a", "aac", "-c:s", "srt",
                "-metadata:s:a:0", "language=eng", "-metadata:s:s:0", "language=eng",
                path);
        });

    /// <summary>Three seconds: mpeg4 video, one mono AAC English track, and two chapters from an ffmetadata input.</summary>
    public Task<string> WithChaptersAsync() => GenerateAsync(
        "fixture-chapters.mkv",
        path =>
        {
            var chaptersPath = Path.ChangeExtension(path, ".txt");
            File.WriteAllText(
                chaptersPath,
                ";FFMETADATA1\n"
                + "[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1500\ntitle=Chapter 1\n"
                + "[CHAPTER]\nTIMEBASE=1/1000\nSTART=1500\nEND=3000\ntitle=Chapter 2\n");
            return Ffmpeg(
                "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                "-i", chaptersPath,
                "-map", "0", "-map", "1", "-map_metadata", "2",
                "-c:v", "mpeg4", "-c:a", "aac",
                "-metadata:s:a:0", "language=eng",
                path);
        });

    /// <summary>Three seconds: mpeg4 video, one AAC track, an ASS subtitle, and an attached font with a mimetype tag.</summary>
    public Task<string> WithAttachmentAsync() => GenerateAsync(
        "fixture-attachment.mkv",
        path =>
        {
            var assPath = Path.ChangeExtension(path, ".ass");
            File.WriteAllText(assPath, "[Script Info]\nScriptType: v4.00+\n\n[Events]\nFormat: Layer, Start, End, Text\nDialogue: 0,0:00:00.00,0:00:03.00,Hello\n");
            var fontPath = Path.ChangeExtension(path, ".ttf");
            File.WriteAllBytes(fontPath, [0x00, 0x01, 0x00, 0x00, 0x00, 0x90, 0x00, 0x03]);
            return Ffmpeg(
                "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                "-i", assPath,
                "-attach", fontPath, "-metadata:s:3", "mimetype=application/x-font-ttf", "-metadata:s:3", "filename=font.ttf",
                "-map", "0", "-map", "1", "-map", "2",
                "-c:v", "mpeg4", "-c:a", "aac", "-c:s", "ass",
                "-metadata:s:a:0", "language=eng",
                "-metadata:s:2", "language=eng",
                path);
        });

    /// <summary>Three seconds: mpeg4 video and one English AAC track marked as a commentary track.</summary>
    public Task<string> WithCommentaryTrackAsync() => GenerateAsync(
        "fixture-commentary.mkv",
        path => Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-map", "0", "-map", "1",
            "-c:v", "mpeg4", "-c:a", "aac",
            "-metadata:s:a:0", "language=eng",
            "-disposition:a:0", "comment",
            path));

    /// <summary>Three seconds: mpeg4 video and one English AAC track carrying statistics tags left by another tool.</summary>
    public Task<string> WithStaleStatisticsTagsAsync() => GenerateAsync(
        "fixture-stale-tags.mkv",
        path => Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-map", "0", "-map", "1",
            "-c:v", "mpeg4", "-c:a", "aac",
            "-metadata:s:a:0", "language=eng",
            "-metadata:s:a:0", "NUMBER_OF_FRAMES=999999",
            "-metadata:s:a:0", "NUMBER_OF_BYTES=123456789",
            "-metadata:s:a:0", "BPS=64000",
            "-metadata:s:a:0", "_STATISTICS_WRITING_APP=FakeTool",
            "-metadata:s:a:0", "_STATISTICS_WRITING_DATE_UTC=2020-01-01 00:00:00",
            "-metadata:s:a:0", "_STATISTICS_TAGS=BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES",
            path));

    private static string[] StandardArguments(string path) =>
        Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=880:duration=3",
            "-map", "0", "-map", "1", "-map", "2",
            "-c:v", "mpeg4", "-c:a", "aac",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=fre",
            path);

    /// <summary>The file at <paramref name="name"/>, made by <paramref name="argvFor"/> (given its path) on first use.</summary>
    private Task<string> GenerateAsync(string name, Func<string, string[]> argvFor) =>
        _generated.GetOrAdd(
            name,
            _ => new Lazy<Task<string>>(async () =>
            {
                var path = Path.Combine(_root, name);
                await RunAsync($"Generating the fixture {name}", argvFor(path)).ConfigureAwait(false);
                return path;
            })).Value;
}
