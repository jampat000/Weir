using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>Running ffmpeg itself: the general-purpose runner with optional progress, and #548's ffmpeg writer.</summary>
public sealed partial class MediaTools
{
    /// <summary>
    /// Runs ffmpeg. Without a progress callback ffmpeg runs quietly; with one, <c>-progress pipe:1</c> is
    /// added and each block is reported. Failures throw <see cref="MediaToolException"/> carrying the tail of stderr.
    /// </summary>
    /// <remarks>
    /// Timeouts (#539 items 4 and 5):
    /// <list type="bullet">
    /// <item>A plain (non-progress) timeout raises <see cref="MediaToolTimeoutException"/>, the same classified
    /// error probing uses. A progress-mode timeout raises <see cref="MediaToolException"/> with
    /// "ffmpeg timed out", the message that path reports.</item>
    /// <item>A timeout, in either mode, kills the whole process tree (<see cref="Processes.ProcessRunner"/>), not
    /// just the direct ffmpeg child: ffmpeg can spawn helper processes (for some hwaccel or filter setups) that
    /// would otherwise survive and keep the output file open.</item>
    /// </list>
    /// The wall-clock limit is enforced by <see cref="Processes.ProcessRunner"/> on a timer independent of stdout activity.
    /// It is only the outer bound, sized to the file by the caller (<see cref="ToolTimeLimits.OverallSeconds"/>). A run that
    /// reports progress is also stopped when it falls silent for <see cref="ToolTimeLimits.SilenceSeconds"/>, so a read
    /// that has stopped is found in minutes while a slow one is left to finish.
    /// </remarks>
    public async Task RunFfmpegAsync(
        IReadOnlyList<string> argv,
        int? timeoutSeconds = FfmpegCommands.FfmpegTimeoutSeconds,
        Action<FfmpegProgressUpdate>? progressCallback = null,
        double? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(argv);
        TimeSpan? timeout = timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        if (progressCallback is null)
        {
            var quiet = await _runner.RunAsync(
                new ProcessRequest
                {
                    Argv = argv,
                    Timeout = timeout,
                    Stdin = ProcessInput.Null,
                    Stdout = ProcessOutput.Discard,
                    Stderr = ProcessOutput.Tail,
                    TailBytes = FfmpegCommands.FfmpegStderrTailBytes,
                },
                cancellationToken).ConfigureAwait(false);
            if (quiet.TimedOut)
            {
                throw new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(argv, timeoutSeconds ?? 0));
            }

            if (quiet.ExitCode != 0)
            {
                throw ProbeOutput.FfmpegFailure(ProbeOutput.TailText(quiet.Stderr));
            }

            return;
        }

        var progressArgv = FfmpegCommands.WithProgress(argv);
        var tracker = new FfmpegProgressTracker(durationSeconds, timeoutSeconds);
        var started = _timeProvider.GetTimestamp();
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = progressArgv,
                // The runner enforces the limit even for an ffmpeg that goes silent, and kills the whole tree.
                Timeout = timeout,
                Stdin = ProcessInput.Null,
                Stderr = ProcessOutput.Tail,
                TailBytes = FfmpegCommands.FfmpegStderrTailBytes,
                IdleTimeout = SilenceLimit,
                OnStdoutLine = line =>
                {
                    var update = tracker.Feed(line, _timeProvider.GetElapsedTime(started).TotalSeconds);
                    if (update is not null)
                    {
                        progressCallback(update);
                    }
                },
            },
            cancellationToken).ConfigureAwait(false);
        switch (result.Timeout)
        {
            case ProcessTimeoutKind.Overall:
                throw new MediaToolException("ffmpeg timed out") { PlainMessage = ToolFailureText.TookTooLong };
            case ProcessTimeoutKind.Idle:
                throw new MediaToolException("the media tool stopped reporting progress") { PlainMessage = ToolFailureText.Stalled };
            case ProcessTimeoutKind.None:
            default:
                break;
        }

        if (result.ExitCode != 0)
        {
            throw ProbeOutput.FfmpegFailure(ProbeOutput.TailText(result.Stderr));
        }
    }

    /// <summary>
    /// Throws for a run the process runner stopped: past its size-based limit (<see cref="MediaToolTimeoutException"/>, as probing
    /// reports a timeout), or silent for <see cref="ToolTimeLimits.SilenceSeconds"/> (<see cref="MediaToolException"/>).
    /// </summary>
    private static void ThrowIfStopped(ProcessResult result, IReadOnlyList<string> argv, int timeoutSeconds)
    {
        switch (result.Timeout)
        {
            case ProcessTimeoutKind.Overall:
                throw new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(argv, timeoutSeconds));
            case ProcessTimeoutKind.Idle:
                throw new MediaToolException("the media tool stopped reporting progress") { PlainMessage = ToolFailureText.Stalled };
            case ProcessTimeoutKind.None:
            default:
                break;
        }
    }

    /// <summary>
    /// #548: ffmpeg's half of <see cref="IRemuxWriter"/>, separate so the writer choice has somewhere to
    /// dispatch to. Validation is deliberately not here: the caller runs it on whichever writer wrote the file.
    /// </summary>
    public Task WriteWithFfmpegAsync(RemuxWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, ffmpeg) = _resolver.Resolve();
        var argv = FfmpegCommands.BuildRemuxArgv(ffmpeg, request.Source, request.Destination, request.Plan, request.FfmpegInputFlags);
        LogFfmpegDebug(FfmpegCommands.DebugSummary(argv));
        return RunFfmpegAsync(
            argv,
            ToolTimeLimits.OverallSeconds(SizeOfFile(request.Source)),
            progressCallback: request.ProgressCallback,
            durationSeconds: request.DurationSeconds,
            cancellationToken: cancellationToken);
    }
}
