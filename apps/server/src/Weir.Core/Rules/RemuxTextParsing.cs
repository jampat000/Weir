using System.Text.RegularExpressions;
using Weir.Core.Json;

namespace Weir.Core.Rules;

/// <summary>Language-tag normalization and the plain-text parsing helpers (CSV language lists, path lines) remux planning reads from stored settings.</summary>
public static partial class RemuxRules
{
    [GeneratedRegex("^([a-z]{2,3})(?:-[a-z0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagRegex();

    /// <summary>The primary subtag of a language tag, lower-cased.</summary>
    public static string NormalizeLang(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return string.Empty;
        }

        var s = Py.Lower(PyStrings.Strip(tag));
        if (s.Length == 0)
        {
            return string.Empty;
        }

        var match = LanguageTagRegex().Match(s);
        return match.Success ? match.Groups[1].Value : PyStrings.Slice(s, 12);
    }

    public static IReadOnlyList<string> ParseSubtitleLangsCsv(string? raw)
    {
        var result = new List<string>();
        foreach (var part in (raw ?? string.Empty).Replace("\n", ",", StringComparison.Ordinal).Split(','))
        {
            // Issue #496: preserves a recognized variant identifier instead of reducing it to its
            // base language; identical to NormalizeLang for every plain code.
            var lang = LanguageVariants.NormalizeLanguageOrVariant(part);
            if (lang.Length > 0 && !result.Contains(lang))
            {
                result.Add(lang);
            }
        }

        return result;
    }

    /// <summary>Non-blank lines, stripped.</summary>
    public static IReadOnlyList<string> ParsePathLines(string? raw)
    {
        var lines = new List<string>();
        foreach (var line in SplitLines(raw ?? string.Empty))
        {
            var s = PyStrings.Strip(line);
            if (s.Length > 0)
            {
                lines.Add(s);
            }
        }

        return lines;
    }

    /// <summary><c>str.splitlines()</c>.</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\n' or '\r' or '\v' or '\f' or '\x1c' or '\x1d' or '\x1e' or '\x85' or '\x2028' or '\x2029')
            {
                lines.Add(text[start..i]);
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                start = i;
            }
            else
            {
                i++;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
