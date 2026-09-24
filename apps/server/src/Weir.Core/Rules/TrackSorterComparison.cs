using System.Globalization;
using System.Text.RegularExpressions;
using Weir.Core.Json;

namespace Weir.Core.Rules;

/// <summary>Parses and evaluates a sorter's match expression (<c>"&gt;=5.1"</c>, <c>"!=true"</c>, a plain text match) against a track's fact.</summary>
public static partial class TrackSorters
{
    // Whitespace here includes U+001C..U+001F, which .NET's \s does not, so saved sorters keep parsing the same way.
    private const string PyWhitespace = "[\\t\\n\\x0b\\x0c\\r\x001c-\x001f \x0085\x00a0\x1680\x2000-\x200a\x2028\x2029\x202f\x205f\x3000]";

    [GeneratedRegex("^" + PyWhitespace + "*(>=|<=|!=|>|<|=)?" + PyWhitespace + "*(.+?)" + PyWhitespace + "*$", RegexOptions.CultureInvariant)]
    private static partial Regex ComparisonRegex();

    /// <summary>
    /// <c>5.1</c> means six channels, and operators write it that way. Null when the text is not a
    /// channel count.
    /// </summary>
    internal static double? ParseChannels(string text)
    {
        var raw = Py.Lower(PyStrings.Strip(text));
        if (raw is "mono" or "1.0")
        {
            return 1.0;
        }

        if (raw is "stereo" or "2.0")
        {
            return 2.0;
        }

        if (raw.Contains('.', StringComparison.Ordinal))
        {
            var dot = raw.IndexOf('.', StringComparison.Ordinal);
            var main = Py.TryIntFromText(raw[..dot]);
            var lfe = Py.TryIntFromText(raw[(dot + 1)..]);
            return main is null || lfe is null ? null : main.Value + lfe.Value;
        }

        return Py.TryIntFromText(raw);
    }

    private static bool Compare(object? actual, string op, string expected, string field)
    {
        if (field == "channels")
        {
            var wanted = ParseChannels(expected);
            var have = NumberOrZero(actual);
            return wanted is not null && NumericCompare(have, op, wanted.Value);
        }

        if (field == "bitrate")
        {
            var normalized = Py.Lower(PyStrings.Strip(expected)).TrimEnd('k').Replace("_", string.Empty, StringComparison.Ordinal);
            if (Py.TryFloatFromText(normalized) is not { } wantedNumber)
            {
                return false;
            }

            if (Py.Lower(PyStrings.Strip(expected)).EndsWith('k'))
            {
                wantedNumber *= 1000;
            }

            return NumericCompare(NumberOrZero(actual), op, wantedNumber);
        }

        if (field is "default" or "forced" or "commentary")
        {
            var wantedBool = Py.Lower(PyStrings.Strip(expected)) is "1" or "true" or "yes" or "on";
            var haveBool = Truthy(actual);
            return op == "!=" ? haveBool != wantedBool : haveBool == wantedBool;
        }

        // Text compares case-insensitively; title is a containment test.
        var haveText = Py.Lower(PyStrings.Strip(Truthy(actual) ? Str(actual) : string.Empty));
        var wantText = Py.Lower(PyStrings.Strip(expected));
        var matched = field == "title" ? haveText.Contains(wantText, StringComparison.Ordinal) : haveText == wantText;
        return op == "!=" ? !matched : matched;
    }

    private static bool NumericCompare(double have, string op, double wanted) => op switch
    {
        ">=" => have >= wanted,
        "<=" => have <= wanted,
        ">" => have > wanted,
        "<" => have < wanted,
        "!=" => have != wanted,
        _ => have == wanted,
    };

    private static (string Operator, string Expected) SplitExpression(string value)
    {
        var match = ComparisonRegex().Match(value);
        if (!match.Success)
        {
            return ("=", PyStrings.Strip(value));
        }

        return (match.Groups[1].Success ? match.Groups[1].Value : "=", match.Groups[2].Value);
    }

    private static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        long n => n != 0,
        string s => s.Length > 0,
        _ => true,
    };

    private static string Str(object? value) => value switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        long n => n.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>A track fact as a number, 0 when falsy.</summary>
    private static double NumberOrZero(object? actual) => (double)LongOrZero(actual);

    private static long LongOrZero(object? actual) => actual switch
    {
        long n => n,
        bool b => b ? 1 : 0,
        string s when s.Length > 0 => Py.IntFromText(s),
        _ => 0,
    };
}
