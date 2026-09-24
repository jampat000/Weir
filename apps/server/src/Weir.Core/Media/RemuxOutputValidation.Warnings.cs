using System.Text.RegularExpressions;
using Weir.Core.Json;

namespace Weir.Core.Media;

/// <summary>
/// #500: normalizing an ffprobe/ffmpeg diagnostic line so the same warning from two process runs compares
/// equal, and finding which of an output's warnings are new relative to its source's.
/// </summary>
public static partial class RemuxOutputValidation
{
    /// <summary>
    /// Pointer addresses, in both forms these lines carry them.
    /// <para>
    /// The <c>0x</c> form is the obvious one. The second alternative is the one that matters in practice:
    /// ffmpeg and ffprobe prefix almost every diagnostic with their own context, and print that pointer
    /// <b>bare</b> — <c>[matroska,webm @ 000002a22ac49500] Could not find codec parameters…</c>. Without this,
    /// only the digit runs inside such an address would be replaced and the letters would survive
    /// (<c>000002a22ac49500</c> → <c>#a#ac#</c>), so the same warning from two process runs would never
    /// normalise alike, <see cref="WarningsNewInOutput"/> would call every one of them new, and any file that made
    /// ffprobe say anything at all would fail its own output validation.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"0x[0-9a-fA-F]+|(?<=@\s)[0-9a-fA-F]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex HexAddressRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRunRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRunRegex();

    /// <summary>Strips the noise a fresh process run adds to an otherwise identical ffprobe/ffmpeg diagnostic line: pointer addresses and any other run of digits (offsets, timestamps, byte counts).</summary>
    public static string NormalizeWarning(string line)
    {
        var text = WireStrings.Strip(line);
        text = HexAddressRegex().Replace(text, "0x#");
        text = NumberRunRegex().Replace(text, "#");
        text = WhitespaceRunRegex().Replace(text, " ");
        return text;
    }

    /// <summary>Non-blank, stripped lines of an ffprobe <c>-v warning</c> stderr capture.</summary>
    public static IReadOnlyList<string> WarningLines(string stderrText)
    {
        var result = new List<string>();
        foreach (var raw in MediaText.SplitLines(stderrText))
        {
            var stripped = WireStrings.Strip(raw);
            if (stripped.Length > 0)
            {
                result.Add(stripped);
            }
        }

        return result;
    }

    /// <summary>
    /// The output's normalized warning lines that have no match among the source's normalized warning lines
    /// (order preserved, duplicates collapsed).
    /// </summary>
    public static IReadOnlyList<string> WarningsNewInOutput(IReadOnlyList<string> sourceWarnings, IReadOnlyList<string> outputWarnings)
    {
        ArgumentNullException.ThrowIfNull(sourceWarnings);
        ArgumentNullException.ThrowIfNull(outputWarnings);
        var sourceNormalized = new HashSet<string>(sourceWarnings.Select(NormalizeWarning), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var line in outputWarnings)
        {
            var normalized = NormalizeWarning(line);
            if (!sourceNormalized.Contains(normalized) && seen.Add(normalized))
            {
                result.Add(line);
            }
        }

        return result;
    }
}
