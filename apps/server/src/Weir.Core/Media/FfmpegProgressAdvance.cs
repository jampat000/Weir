using System.Globalization;

namespace Weir.Core.Media;

/// <summary>
/// Tells, line by line, whether an ffmpeg <c>-progress pipe:1</c> stream is getting anywhere. ffmpeg 9 keeps printing a
/// <c>progress=continue</c> block every half second while it is stuck on a read that does not return, with the same
/// <c>out_time_us</c>, <c>total_size</c> and <c>frame</c> every time, so that a block arrived says nothing: only a block in which
/// one of those has grown does.
/// </summary>
public sealed class FfmpegProgressAdvance
{
    private const string BlockEnd = "progress";
    private const string EndValue = "end";

    private readonly Dictionary<string, long> _block = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _best = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="rawLine"/> is the last line ffmpeg writes: the block that says it has finished.</summary>
    public static bool IsEnd(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        return rawLine.AsSpan().Trim().SequenceEqual("progress=end");
    }

    /// <summary>
    /// Feeds one raw stdout line. True when it closes a block in which output time, output size or the frame count is further
    /// than in every block before it, or when it closes the final block.
    /// </summary>
    public bool Feed(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        var line = rawLine.AsSpan().Trim();
        var equals = line.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        var key = line[..equals].ToString();
        var value = line[(equals + 1)..];
        if (key != BlockEnd)
        {
            if (key is "out_time_us" or "total_size" or "frame" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                _block[key] = number;
            }

            return false;
        }

        var advanced = value.SequenceEqual(EndValue);
        foreach (var (name, number) in _block)
        {
            if (!_best.TryGetValue(name, out var best) || number > best)
            {
                _best[name] = number;
                advanced = true;
            }
        }

        _block.Clear();
        return advanced;
    }
}
