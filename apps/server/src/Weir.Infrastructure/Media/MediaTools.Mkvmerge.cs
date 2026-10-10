using System.Text;
using System.Text.Json;
using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>#548: running mkvmerge itself — identification and the write with <c>--gui-mode</c> progress.</summary>
public sealed partial class MediaTools
{
    /// <summary>#548: <c>mkvmerge -J</c>, which reads headers only, for the track and attachment numbering.</summary>
    public async Task<MkvmergeIdentification> IdentifyMkvmergeAsync(string mkvmergeBin, string src, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mkvmergeBin);
        ArgumentNullException.ThrowIfNull(src);
        var argv = MkvmergeCommands.BuildIdentifyArgv(mkvmergeBin, src);
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = argv,
                Timeout = TimeSpan.FromSeconds(MkvmergeCommands.IdentifyTimeoutSeconds),
                Stdin = ProcessInput.Null,
                Stdout = ProcessOutput.Capture,
                Stderr = ProcessOutput.Tail,
                TailBytes = MkvmergeCommands.StderrTailBytes,
            },
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(argv, MkvmergeCommands.IdentifyTimeoutSeconds));
        }

        // mkvmerge's exit code 1 means "identified, with warnings", which is still a usable answer.
        if (result.ExitCode is not 0 and not MkvmergeCommands.ExitCodeWarnings)
        {
            var detail = ProbeOutput.TailText(result.Stderr);
            throw new MediaToolException("mkvmerge could not identify the file: " + detail) { PlainMessage = ToolFailureText.ForToolText(detail) };
        }

        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            return MkvmergeCommands.ParseIdentification(document.RootElement);
        }
        catch (JsonException error)
        {
            throw new MediaToolException("mkvmerge's identification output was not valid JSON.", error);
        }
    }

    /// <summary>
    /// #548: runs one mkvmerge write, reporting <c>--gui-mode</c>'s percentage through the same
    /// <see cref="FfmpegProgressUpdate"/> the ffmpeg path reports (see
    /// <see cref="MkvmergeCommands.TryParseProgressPercent"/> for what mkvmerge does and does not tell us).
    /// </summary>
    public async Task RunMkvmergeAsync(
        IReadOnlyList<string> argv,
        Action<FfmpegProgressUpdate>? progressCallback = null,
        long sourceBytes = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(argv);
        LogFfmpegDebug(MkvmergeCommands.DebugSummary(argv));
        var started = _timeProvider.GetTimestamp();
        var timeoutSeconds = ToolTimeLimits.OverallSeconds(sourceBytes);
        var furthest = -1.0;
        var diagnostics = new StdoutLines();
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = argv,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                // --gui-mode reports whole percents, and only a percent further than the last is progress. A source this large
                // takes longer than the usual silence limit to get from one percent to the next at the slowest rate a write is
                // allowed, so the limit grows with it.
                IdleTimeout = TimeSpan.FromSeconds(Math.Max(SilenceLimit.TotalSeconds, ToolTimeLimits.PercentStepSeconds(sourceBytes))),
                MarksProgress = line =>
                {
                    if (MkvmergeCommands.TryParseProgressPercent(line) is not { } percent || percent <= furthest)
                    {
                        return false;
                    }

                    furthest = percent;
                    return true;
                },
                Stdin = ProcessInput.Null,
                Stdout = ProcessOutput.Discard,
                Stderr = ProcessOutput.Tail,
                TailBytes = MkvmergeCommands.StderrTailBytes,
                OnStdoutLine = line =>
                {
                    if (MkvmergeCommands.TryParseProgressPercent(line) is not { } percent)
                    {
                        // mkvmerge writes its diagnostics to stdout as well as its progress, so the tail that a failure
                        // message needs is collected from the lines as they arrive rather than from Stderr alone.
                        diagnostics.Add(line);
                        return;
                    }

                    progressCallback?.Invoke(new FfmpegProgressUpdate
                    {
                        Percent = percent,
                        ElapsedSeconds = (long)_timeProvider.GetElapsedTime(started).TotalSeconds,
                        Progress = percent >= 100 ? "end" : "continue",
                    });
                },
            },
            cancellationToken).ConfigureAwait(false);
        ThrowIfStopped(result, argv, timeoutSeconds);

        if (result.ExitCode is not 0 and not MkvmergeCommands.ExitCodeWarnings)
        {
            var detail = ProbeOutput.TailText(result.Stderr);
            if (detail.Length == 0)
            {
                detail = ProbeOutput.TailText(Encoding.UTF8.GetBytes(diagnostics.Text));
            }

            throw new MediaToolException("mkvmerge failed: " + detail) { PlainMessage = ToolFailureText.ForToolText(detail) };
        }
    }
}
