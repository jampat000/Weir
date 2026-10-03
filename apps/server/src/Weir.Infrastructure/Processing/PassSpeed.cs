using System.Globalization;

namespace Weir.Infrastructure.Processing;

/// <summary>Reads the speed text a pass reports.</summary>
public static class PassSpeed
{
    private const string MultipleSuffix = "x";

    /// <summary>
    /// The number in ffmpeg's <c>148x</c>: how many seconds of the file are written each second. Null for text that is not
    /// one, such as a copy's <c>12.5 MB/s</c> or ffmpeg's <c>N/A</c> before it has measured anything.
    /// </summary>
    public static double? Multiple(string? speed)
    {
        var text = speed?.Trim();
        return text is { Length: > 1 } && text.EndsWith(MultipleSuffix, StringComparison.Ordinal)
            && double.TryParse(text[..^MultipleSuffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiple)
            && double.IsFinite(multiple) && multiple >= 0
            ? multiple
            : null;
    }
}
