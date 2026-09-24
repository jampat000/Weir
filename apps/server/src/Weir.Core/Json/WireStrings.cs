using System.Globalization;
using System.Text;

namespace Weir.Core.Json;

/// <summary>
/// String rules that differ from .NET's defaults: code-point length and slicing, the Unicode whitespace
/// set used for trimming, and the quoted display form. Length limits, trimmed values and quoted text in
/// messages and stored data depend on them, so they stay the same as earlier releases wrote them.
/// </summary>
/// <remarks>
/// Lengths and slices count code points, not UTF-16 units: a surrogate pair counts as one code point,
/// and a lone surrogate counts as one too and is kept as it is.
/// </remarks>
public static class WireStrings
{
    /// <summary>Whether one UTF-16 unit is whitespace (every whitespace code point in the set is in the BMP).</summary>
    public static bool IsSpace(char c) => c switch
    {
        '\t' or '\n' or '\v' or '\f' or '\r' or ' ' => true,
        '\x1c' or '\x1d' or '\x1e' or '\x1f' or '\x85' or '\xa0' => true,
        '\x1680' or '\x2028' or '\x2029' or '\x202f' or '\x205f' or '\x3000' => true,
        >= '\x2000' and <= '\x200a' => true,
        _ => false,
    };

    /// <summary>Trims <see cref="IsSpace"/> whitespace from both ends.</summary>
    public static string Strip(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var start = 0;
        var end = value.Length;
        while (start < end && IsSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>The length in code points, not UTF-16 units.</summary>
    public static int Length(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsSurrogatePair(value, i))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    /// <summary>The first <paramref name="count"/> code points, never splitting a surrogate pair.</summary>
    public static string Slice(string value, int count)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (count <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= count)
        {
            return value;
        }

        var taken = 0;
        var index = 0;
        while (index < value.Length && taken < count)
        {
            index += char.IsSurrogatePair(value, index) ? 2 : 1;
            taken++;
        }

        return value[..index];
    }

    /// <summary>
    /// The quoted display form: single quotes unless the text holds a single quote and no double quote;
    /// non-printable code points escaped as <c>\xhh</c>, <c>\uhhhh</c> or <c>\Uhhhhhhhh</c>.
    /// </summary>
    public static string Repr(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var quote = value.Contains('\'', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var builder = new StringBuilder(value.Length + 2);
        builder.Append(quote);
        for (var i = 0; i < value.Length; i++)
        {
            int cp;
            Rune? rune = null;
            if (char.IsSurrogatePair(value, i))
            {
                cp = char.ConvertToUtf32(value[i], value[i + 1]);
                rune = new Rune(cp);
                i++;
            }
            else
            {
                cp = value[i];
                if (!char.IsSurrogate(value[i]))
                {
                    rune = new Rune(cp);
                }
            }

            if (cp == quote || cp == '\\')
            {
                builder.Append('\\').Append((char)cp);
            }
            else if (cp == '\t')
            {
                builder.Append("\\t");
            }
            else if (cp == '\n')
            {
                builder.Append("\\n");
            }
            else if (cp == '\r')
            {
                builder.Append("\\r");
            }
            else if (cp < 0x20 || cp == 0x7f)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\x{cp:x2}");
            }
            else if (cp < 0x7f || (rune is { } r && IsPrintable(r)))
            {
                builder.Append(rune!.Value.ToString());
            }
            else if (cp < 0x100)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\x{cp:x2}");
            }
            else if (cp < 0x10000)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\u{cp:x4}");
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\U{cp:x8}");
            }
        }

        builder.Append(quote);
        return builder.ToString();
    }

    /// <summary>Whether a code point is printed as-is: space is the only printable separator.</summary>
    private static bool IsPrintable(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.SpaceSeparator => rune.Value == ' ',
        _ => true,
    };
}
