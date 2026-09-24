using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>Output validation (the golden-file audio-count and duration check) and the full-read integrity check.</summary>
public sealed partial class MediaTools
{
    /// <summary>Probes the staged output and checks audio count and duration.</summary>
    public async Task ValidateRemuxOutputAsync(string path, int expectedAudio = 0, double? expectedDurationSeconds = null, CancellationToken cancellationToken = default)
    {
        var data = await FfprobeJsonAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
        ProbeOutput.ValidateRemuxOutput(data, expectedAudio, expectedDurationSeconds);
    }

    /// <summary>
    /// Reads the primary video from start to finish and throws
    /// <see cref="MediaCompletenessException"/> for damaged or truncated input.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="expectedDurationSeconds">
    /// The probed duration, when known. The exit code alone is not enough: a Matroska file cut off mid-cluster
    /// keeps its header duration, and a truncated demux that only warns (see
    /// <see cref="ProbeOutput.IntegrityIncompleteMarkers"/>) still exits 0 (#539 item 3). When a duration is
    /// given, the last timestamp the demux actually reached (from <c>-progress pipe:1</c>) is compared against it
    /// with the same tolerance as <see cref="ProbeOutput.ValidateRemuxOutput"/>, but only when ffmpeg reported at
    /// least one timestamp, since a source with no video stream or one <c>-err_detect explode</c> kills before
    /// the first frame reports none.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <remarks>
    /// The full read runs on Windows too (#539 item 5). Windows write handles are not a reliable sign that a
    /// file is finished: an SMB share, a WSL2 bind mount, or a downloader that preallocates then writes can all
    /// leave a reader able to open a file that is not finished. Callers should not add a Windows skip without a
    /// concrete, current reason to.
    /// </remarks>
    public async Task ValidateMediaIntegrityAsync(string path, double? expectedDurationSeconds = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (_, ffmpeg) = _resolver.Resolve();
        var baseArgv = FfmpegCommands.BuildIntegrityArgv(ffmpeg, path);
        var wantsProgress = expectedDurationSeconds is > 0;
        var argv = wantsProgress ? FfmpegCommands.WithProgress(baseArgv) : baseArgv;
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = argv,
                Timeout = TimeSpan.FromSeconds(FfmpegCommands.FfmpegTimeoutSeconds),
                Stdin = ProcessInput.Null,
                Stdout = wantsProgress ? ProcessOutput.Capture : ProcessOutput.Discard,
                Stderr = ProcessOutput.Capture,
            },
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(argv, FfmpegCommands.FfmpegTimeoutSeconds));
        }

        var stderrText = ProbeOutput.CapturedText(result.Stderr);
        if (result.ExitCode != 0)
        {
            throw ProbeOutput.IntegrityFailure(stderrText);
        }

        if (ProbeOutput.HasIntegrityIncompleteMarker(stderrText))
        {
            throw ProbeOutput.IntegrityFailure(stderrText);
        }

        if (expectedDurationSeconds is { } expected && expected > 0
            && ProbeOutput.LastProgressOutTimeSeconds(ProbeOutput.CapturedText(result.Stdout)) is { } decoded)
        {
            var tolerance = Math.Max(5.0, expected * 0.01);
            if (decoded < expected - tolerance)
            {
                throw ProbeOutput.IntegrityShortfall(decoded, expected);
            }
        }
    }
}
