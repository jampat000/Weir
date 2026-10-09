using System.Text.Json;
using Weir.Core.Media;
using Weir.Core.Rules;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests.Media;

/// <summary>
/// How long the media tools are given: a bound sized to the file, and a limit on silence for a tool that reports progress,
/// so a big file on a slow drive is left to finish and only a run that has stopped is cut off.
/// </summary>
public sealed class MediaToolTimeLimitTests
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    private static readonly string StandInPath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Weir.TestChild.exe" : "Weir.TestChild");

    private static MediaTools Tools(IProcessRunner runner, long fileBytes = 100, TimeSpan? silence = null) =>
        new(runner, new FixedResolver(), new ListLogger<MediaTools>(), TimeProvider.System, path => new MediaFileState(path, true, true, fileBytes, 0))
        {
            SilenceLimit = silence ?? TimeSpan.FromSeconds(ToolTimeLimits.SilenceSeconds),
        };

    private static ScriptedRunner Silent(ProcessTimeoutKind timeout = ProcessTimeoutKind.None) =>
        new(_ => new ScriptedRun { Timeout = timeout });

    private static RemuxWriteRequest WriteRequest(Action<FfmpegProgressUpdate>? progress) =>
        new(
            "source.mkv",
            "output.mkv",
            new RemuxPlan { VideoIndices = [0], Audio = [new PlannedTrack { InputIndex = 1, LangLabel = "eng", Default = true }], Subtitles = [] },
            JsonDocument.Parse("{}").RootElement,
            progress);

    [Fact]
    public void The_overall_bound_grows_with_the_size_of_the_file_from_the_least_it_is_ever_given()
    {
        Assert.Equal(FfmpegCommands.FfmpegTimeoutSeconds, ToolTimeLimits.OverallSeconds(0));
        Assert.Equal(FfmpegCommands.FfmpegTimeoutSeconds, ToolTimeLimits.OverallSeconds(-5));
        Assert.Equal(FfmpegCommands.FfmpegTimeoutSeconds + 80 * 1024, ToolTimeLimits.OverallSeconds(80 * Gibibyte));
        Assert.Equal(int.MaxValue, ToolTimeLimits.OverallSeconds(long.MaxValue));
    }

    [Fact]
    public void The_probe_bound_is_the_usual_one_until_the_operator_raises_the_probe_size()
    {
        Assert.Equal(FfmpegCommands.FfprobeTimeoutSeconds, ToolTimeLimits.ProbeSeconds(FfmpegCommands.DefaultProbeSizeMb));
        Assert.Equal(FfmpegCommands.FfprobeTimeoutSeconds, ToolTimeLimits.ProbeSeconds(1));
        Assert.Equal(FfmpegCommands.FfprobeTimeoutSeconds + 1014, ToolTimeLimits.ProbeSeconds(1024));
    }

    [Fact]
    public async Task A_big_file_is_given_longer_to_write_than_the_least_and_a_run_that_reports_progress_may_not_fall_silent()
    {
        var runner = Silent();

        await Tools(runner, fileBytes: 80 * Gibibyte).WriteWithFfmpegAsync(WriteRequest(_ => { }));

        var request = Assert.Single(runner.Requests);
        Assert.Equal(TimeSpan.FromSeconds(FfmpegCommands.FfmpegTimeoutSeconds + 80 * 1024), request.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(ToolTimeLimits.SilenceSeconds), request.IdleTimeout);
    }

    [Fact]
    public async Task A_write_that_stalls_is_reported_as_stalled_not_as_taking_too_long()
    {
        var error = await Assert.ThrowsAsync<MediaToolException>(
            () => Tools(Silent(ProcessTimeoutKind.Idle)).WriteWithFfmpegAsync(WriteRequest(_ => { })));

        Assert.Equal(ToolFailureText.Stalled, ToolFailureText.Plain(error));
    }

    [Fact]
    public async Task A_write_past_its_size_based_bound_is_reported_as_taking_too_long()
    {
        var error = await Assert.ThrowsAsync<MediaToolException>(
            () => Tools(Silent(ProcessTimeoutKind.Overall)).WriteWithFfmpegAsync(WriteRequest(_ => { })));

        Assert.Equal(ToolFailureText.TookTooLong, ToolFailureText.Plain(error));
    }

    [Fact]
    public async Task An_ffmpeg_that_keeps_reporting_is_left_to_run_for_longer_than_the_silence_limit()
    {
        // 40 lines 100 ms apart run for about four seconds, over the three-second limit, but never fall silent for it.
        var tools = Tools(new ProcessRunner(), silence: TimeSpan.FromSeconds(3));

        await tools.RunFfmpegAsync([StandInPath, "chatter", "100", "40"], progressCallback: _ => { });
    }

    [Fact]
    public async Task An_ffmpeg_that_stops_reporting_part_way_is_stopped_for_the_silence()
    {
        var tools = Tools(new ProcessRunner(), silence: TimeSpan.FromSeconds(2));

        var error = await Assert.ThrowsAsync<MediaToolException>(
            () => tools.RunFfmpegAsync([StandInPath, "chatter-then-stall", "50", "5"], progressCallback: _ => { }));

        Assert.Equal(ToolFailureText.Stalled, ToolFailureText.Plain(error));
    }

    [Fact]
    public async Task An_mkvmerge_write_is_sized_to_the_file_and_may_not_fall_silent()
    {
        var runner = Silent();

        await Tools(runner).RunMkvmergeAsync(["mkvmerge"], progressCallback: null, sourceBytes: 10 * Gibibyte);

        var request = Assert.Single(runner.Requests);
        Assert.Equal(TimeSpan.FromSeconds(MkvmergeCommands.MkvmergeTimeoutSeconds + 10 * 1024), request.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(ToolTimeLimits.SilenceSeconds), request.IdleTimeout);
    }

    [Fact]
    public async Task An_mkvmerge_write_that_stalls_is_reported_as_stalled()
    {
        var error = await Assert.ThrowsAsync<MediaToolException>(
            () => Tools(Silent(ProcessTimeoutKind.Idle)).RunMkvmergeAsync(["mkvmerge"], progressCallback: null));

        Assert.Equal(ToolFailureText.Stalled, ToolFailureText.Plain(error));
    }

    [Fact]
    public async Task An_mkvmerge_that_keeps_reporting_is_left_to_run_for_longer_than_the_silence_limit()
    {
        var tools = Tools(new ProcessRunner(), silence: TimeSpan.FromSeconds(3));

        await tools.RunMkvmergeAsync([StandInPath, "chatter", "100", "40"], progressCallback: null);
    }

    [Fact]
    public async Task A_full_read_with_progress_is_sized_to_the_file_and_may_not_fall_silent()
    {
        var runner = Silent();

        await Tools(runner, fileBytes: 20 * Gibibyte).ValidateMediaIntegrityAsync("movie.mkv", expectedDurationSeconds: null);
        await Tools(runner, fileBytes: 20 * Gibibyte).ValidateMediaIntegrityAsync("movie.mkv", expectedDurationSeconds: 600);

        Assert.Equal(2, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.Equal(TimeSpan.FromSeconds(FfmpegCommands.FfmpegTimeoutSeconds + 20 * 1024), request.Timeout));
        Assert.Null(runner.Requests[0].IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(ToolTimeLimits.SilenceSeconds), runner.Requests[1].IdleTimeout);
    }

    [Fact]
    public async Task A_full_read_that_stalls_is_reported_as_stalled()
    {
        var error = await Assert.ThrowsAsync<MediaToolException>(
            () => Tools(Silent(ProcessTimeoutKind.Idle)).ValidateMediaIntegrityAsync("movie.mkv", expectedDurationSeconds: 600));

        Assert.Equal(ToolFailureText.Stalled, ToolFailureText.Plain(error));
    }

    [Fact]
    public async Task Measuring_the_kept_streams_is_sized_to_the_file_and_may_not_fall_silent()
    {
        var runner = Silent();
        var plan = new RemuxPlan { VideoIndices = [0], Audio = [], Subtitles = [] };

        await Tools(runner, fileBytes: 4 * Gibibyte).MeasureKeptStreamsDurationAsync("movie.mkv", plan);

        var request = Assert.Single(runner.Requests);
        Assert.Equal(TimeSpan.FromSeconds(FfmpegCommands.FfmpegTimeoutSeconds + 4 * 1024), request.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(ToolTimeLimits.SilenceSeconds), request.IdleTimeout);
    }

    [Fact]
    public async Task A_raised_probe_size_is_given_the_time_to_be_read()
    {
        var runner = new ScriptedRunner(_ => new ScriptedRun { Stdout = """{"streams":[]}"""u8.ToArray() });

        await Tools(runner).FfprobeJsonAsync("movie.mkv", probeSizeMb: 1024);
        await Tools(runner).FfprobeJsonAsync("movie.mkv");

        Assert.Equal(TimeSpan.FromSeconds(FfmpegCommands.FfprobeTimeoutSeconds + 1014), runner.Requests[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(FfmpegCommands.FfprobeTimeoutSeconds), runner.Requests[1].Timeout);
    }
}
