using System.Text.RegularExpressions;

namespace Weir.Core.Jobs;

/// <summary>
/// The names of temporary files Weir itself creates, so crash recovery and sweeps delete only those
/// (#534). Nothing else in a work or output folder is ever matched.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Remux output: <c>{stem}.processing.{8 characters of [a-z0-9_]}{suffix}</c> in the library's work
/// folder, where the suffix is the source's (or <c>.mkv</c> when it has none).</item>
/// <item>The dry-run placeholder <c>dry-run-ffmpeg-destination-placeholder.mkv</c>.</item>
/// <item>Atomic output writes: a hidden <c>.{name}.{8 characters}.partial</c> beside the destination,
/// matched by the <c>.*.partial</c> name test.</item>
/// </list>
/// Matching is the exact created shape, not any name containing <c>.processing.</c>, so an operator's
/// own <c>Film.processing.notes.txt</c> survives.
/// </remarks>
public static partial class WeirTempFiles
{
    public const string DryRunPlaceholderName = "dry-run-ffmpeg-destination-placeholder.mkv";

    /// <summary>The random part of a temp name: its characters and length.</summary>
    private const string RandomPart = "[a-z0-9_]{8}";

    /// <summary>Any remux temp output name Weir creates, for any source.</summary>
    public static bool IsRemuxTempName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName == DryRunPlaceholderName || RemuxTempName().IsMatch(fileName);
    }

    /// <summary>
    /// The remux temp output names for one source file: <c>{stem}.processing.XXXXXXXX{suffix}</c>, where
    /// stem and suffix are split as <see cref="StemAndSuffix"/> splits them.
    /// </summary>
    public static Regex RemuxTempNameFor(string relativeMediaPath)
    {
        ArgumentNullException.ThrowIfNull(relativeMediaPath);
        var name = BaseName(relativeMediaPath);
        var (stem, suffix) = StemAndSuffix(name);
        var effectiveSuffix = suffix.Length == 0 ? ".mkv" : suffix;
        return new Regex(
            "^" + Regex.Escape(stem) + @"\.processing\." + RandomPart + Regex.Escape(effectiveSuffix) + "$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>The <c>.*.partial</c> name test: a hidden name ending in <c>.partial</c>.</summary>
    public static bool IsPartialOutputName(string fileName, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // "." + "*" + ".partial": at least nine characters, the leading dot not shared with the suffix.
        return fileName.Length >= 1 + ".partial".Length &&
               fileName.StartsWith('.') &&
               fileName.EndsWith(".partial", comparison);
    }

    /// <summary>The last path segment for either separator, ignoring trailing separators.</summary>
    public static string BaseName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    /// <summary>
    /// Stem and suffix: the last dot splits them unless it is the first or last character, in which case
    /// there is no suffix (so <c>.hidden</c> and <c>name.</c> have none).
    /// </summary>
    public static (string Stem, string Suffix) StemAndSuffix(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var index = name.LastIndexOf('.');
        if (index <= 0 || index == name.Length - 1)
        {
            return (name, string.Empty);
        }

        return (name[..index], name[index..]);
    }

    [GeneratedRegex(@"^.+\.processing\.[a-z0-9_]{8}(\.[^.]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RemuxTempName();
}
