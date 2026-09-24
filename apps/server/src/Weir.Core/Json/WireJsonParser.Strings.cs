using System.Text;

namespace Weir.Core.Json;

/// <summary>Scans a JSON string literal's body, including backslash and <c>\uXXXX</c>/surrogate-pair escapes.</summary>
public static partial class WireJsonParser
{
    /// <summary>
    /// Scans a string body, refusing raw control characters; <paramref name="start"/> is just after the opening quote.
    /// </summary>
    private static string ScanString(string s, int start, out int end)
    {
        var begin = start - 1;
        var builder = new StringBuilder();
        var next = start;
        var len = s.Length;
        while (true)
        {
            var chunkStart = next;
            while (next < len)
            {
                var c = s[next];
                if (c is '"' or '\\')
                {
                    break;
                }

                if (c <= 0x1F)
                {
                    throw new WireJsonDecodeException("Invalid control character at", next);
                }

                next++;
            }

            if (next >= len)
            {
                throw new WireJsonDecodeException("Unterminated string starting at", begin);
            }

            builder.Append(s, chunkStart, next - chunkStart);
            next++;
            if (s[next - 1] == '"')
            {
                end = next;
                return builder.ToString();
            }

            if (next == len)
            {
                throw new WireJsonDecodeException("Unterminated string starting at", begin);
            }

            var escape = s[next];
            if (escape != 'u')
            {
                end = next + 1;
                char? mapped = escape switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => null,
                };
                if (mapped is null)
                {
                    throw new WireJsonDecodeException("Invalid \\escape", end - 2);
                }

                builder.Append(mapped.Value);
                next = end;
                continue;
            }

            next++;
            var hexEnd = next + 4;
            if (hexEnd >= len)
            {
                throw new WireJsonDecodeException("Invalid \\uXXXX escape", next - 1);
            }

            var code = ReadHex4(s, next, hexEnd);
            next = hexEnd;
            if (char.IsHighSurrogate((char)code) && hexEnd + 6 < len && s[hexEnd] == '\\' && s[hexEnd + 1] == 'u')
            {
                var low = ReadHex4(s, hexEnd + 2, hexEnd + 6);
                if (char.IsLowSurrogate((char)low))
                {
                    builder.Append((char)code).Append((char)low);
                    next = hexEnd + 6;
                    continue;
                }
            }

            builder.Append((char)code);
        }
    }

    private static int ReadHex4(string s, int from, int to)
    {
        var value = 0;
        for (var i = from; i < to; i++)
        {
            var digit = s[i] switch
            {
                >= '0' and <= '9' => s[i] - '0',
                >= 'a' and <= 'f' => s[i] - 'a' + 10,
                >= 'A' and <= 'F' => s[i] - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                throw new WireJsonDecodeException("Invalid \\uXXXX escape", to - 5);
            }

            value = (value << 4) | digit;
        }

        return value;
    }
}
