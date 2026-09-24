namespace Weir.Core.Json;

/// <summary>Scans an object or array body, calling back into the scanner for each member's value.</summary>
public static partial class WireJsonParser
{
    private static WireObject ParseObject(string s, int idx, int depth, out int end)
    {
        var result = new WireObject();
        idx = SkipWhitespace(s, idx);
        if (idx < s.Length && s[idx] == '}')
        {
            end = idx + 1;
            return result;
        }

        while (true)
        {
            if (idx >= s.Length || s[idx] != '"')
            {
                throw new WireJsonDecodeException("Expecting property name enclosed in double quotes", idx);
            }

            var key = ScanString(s, idx + 1, out idx);
            idx = SkipWhitespace(s, idx);
            if (idx >= s.Length || s[idx] != ':')
            {
                throw new WireJsonDecodeException("Expecting ':' delimiter", idx);
            }

            idx = SkipWhitespace(s, idx + 1);
            var value = ScanOnceOrExpectingValue(s, idx, depth, out idx);
            result.Set(key, value);
            idx = SkipWhitespace(s, idx);
            if (idx < s.Length && s[idx] == '}')
            {
                end = idx + 1;
                return result;
            }

            if (idx >= s.Length || s[idx] != ',')
            {
                throw new WireJsonDecodeException("Expecting ',' delimiter", idx);
            }

            idx = SkipWhitespace(s, idx + 1);
        }
    }

    private static WireArray ParseArray(string s, int idx, int depth, out int end)
    {
        var result = new WireArray();
        idx = SkipWhitespace(s, idx);
        if (idx < s.Length && s[idx] == ']')
        {
            end = idx + 1;
            return result;
        }

        while (true)
        {
            var value = ScanOnceOrExpectingValue(s, idx, depth, out idx);
            result.Items.Add(value);
            idx = SkipWhitespace(s, idx);
            if (idx < s.Length && s[idx] == ']')
            {
                end = idx + 1;
                return result;
            }

            if (idx >= s.Length || s[idx] != ',')
            {
                throw new WireJsonDecodeException("Expecting ',' delimiter", idx);
            }

            idx = SkipWhitespace(s, idx + 1);
        }
    }
}
