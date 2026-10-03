using System.Globalization;
using System.Text;

namespace Weir.Core.MediaManagers;

/// <summary>Form-encoded query strings, as the media managers and download clients read them.</summary>
public static class QueryStrings
{
    /// <summary>A query string of the given pairs, each side encoded by <see cref="QuotePlus"/>.</summary>
    public static string Encode(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        return string.Join('&', pairs.Select(pair => QuotePlus(pair.Key) + "=" + QuotePlus(pair.Value)));
    }

    /// <summary>Percent-encodes UTF-8 bytes except ASCII letters, digits and <c>_.-~</c>; a space becomes <c>+</c>.</summary>
    public static string QuotePlus(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '~')
            {
                builder.Append(c);
            }
            else if (c == ' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
