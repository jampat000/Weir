using System.Globalization;
using Weir.Core.Json;
using Weir.Core.Rules;

namespace Weir.Core.Media;

/// <summary>One progress report from an ffmpeg run.</summary>
public sealed record FfmpegProgressUpdate
{
    /// <summary>0..99 while running, 100 at the end; null when the duration is unknown.</summary>
    public double? Percent { get; init; }

    public long? EtaSeconds { get; init; }

    public long ElapsedSeconds { get; init; }

    /// <summary><c>out_time_ms</c> in seconds; null when ffmpeg reported something that is not a number.</summary>
    public double? ProcessedSeconds { get; init; }

    public string? Speed { get; init; }

    /// <summary>ffmpeg's own <c>progress=</c> value: <c>continue</c> or <c>end</c>.</summary>
    public required string Progress { get; init; }
}

/// <summary>
/// The line-by-line logic of an ffmpeg run's progress loop: <c>-progress pipe:1</c> output is
/// <c>key=value</c> lines, and each <c>progress=</c> line closes a block. Time is supplied by the
/// caller as seconds since the process started, read once per line.
/// </summary>
public sealed class FfmpegProgressTracker
{
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
    private readonly double? _durationSeconds;
    private readonly double? _timeoutSeconds;

    public FfmpegProgressTracker(double? durationSeconds, double? timeoutSeconds = FfmpegCommands.FfmpegTimeoutSeconds)
    {
        _durationSeconds = durationSeconds;
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    /// Feeds one raw stdout line. Returns the update to report, or null. Throws <see cref="MediaToolException"/>
    /// when ffmpeg must be stopped: past the time limit, or projecting more than twelve hours remaining.
    /// </summary>
    public FfmpegProgressUpdate? Feed(string rawLine, double secondsSinceStart)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        var elapsed = HigherOf(0.0, secondsSinceStart);
        if (_timeoutSeconds is { } timeout && elapsed > timeout)
        {
            throw new MediaToolException("ffmpeg timed out");
        }

        var line = WireStrings.Strip(rawLine);
        var equals = line.IndexOf('=', StringComparison.Ordinal);
        if (line.Length == 0 || equals < 0)
        {
            return null;
        }

        var key = line[..equals];
        var value = line[(equals + 1)..];
        _fields[key] = value;
        if (key != "progress")
        {
            return null;
        }

        double? outTime = null;
        var outTimeText = _fields.TryGetValue("out_time_ms", out var ms) ? ms : "0";
        if (RulesJson.TryFloatFromText(outTimeText) is { } micros)
        {
            outTime = HigherOf(0.0, micros / 1_000_000.0);
        }

        var ended = value == "end";
        double? percent = null;
        long? eta = null;
        if (_durationSeconds is { } duration && duration != 0 && duration > 0 && outTime is { } processed)
        {
            percent = HigherOf(0.0, LowerOf(ended ? 100.0 : 99.0, processed / duration * 100.0));
            if (percent > 0 && !ended)
            {
                var totalEstimate = elapsed / (percent.Value / 100.0);
                eta = (long)HigherOf(0, TruncateToWhole(totalEstimate - elapsed));
            }
        }

        if (ended)
        {
            percent = 100.0;
            eta = 0;
        }

        if (elapsed >= FfmpegCommands.FfmpegSlowGraceSeconds && eta is { } projected && projected > FfmpegCommands.FfmpegMaxProjectedRemainingSeconds)
        {
            throw new MediaToolException(
                "ffmpeg was stopped because progress projected more than 12 hours remaining. "
                + "The input may be malformed, mislabeled, or unreadable at a usable speed.");
        }

        return new FfmpegProgressUpdate
        {
            Percent = percent,
            EtaSeconds = eta,
            ElapsedSeconds = (long)TruncateToWhole(elapsed),
            ProcessedSeconds = outTime,
            Speed = _fields.TryGetValue("speed", out var speed) ? speed : null,
            Progress = value,
        };
    }

    /// <summary>The first argument unless the second is strictly greater (so a NaN second argument never wins).</summary>
    private static double HigherOf(double a, double b) => b > a ? b : a;

    /// <summary>The first argument unless the second is strictly smaller.</summary>
    private static double LowerOf(double a, double b) => b < a ? b : a;

    /// <summary>Truncates toward zero; throws for NaN, infinity, or a result that will not fit a long.</summary>
    private static double TruncateToWhole(double value)
    {
        if (double.IsNaN(value))
        {
            throw new RulesInputException("ValueError", "An ffmpeg progress time is not a number.");
        }

        if (double.IsInfinity(value))
        {
            throw new RulesInputException("OverflowError", "An ffmpeg progress time is infinite.");
        }

        var truncated = Math.Truncate(value);
        if (truncated is >= 9.2233720368547758e18 or < -9.2233720368547758e18)
        {
            throw new RulesInputException("OverflowError", string.Create(CultureInfo.InvariantCulture, $"The ffmpeg progress time {value} is too large to use."));
        }

        return truncated;
    }
}
