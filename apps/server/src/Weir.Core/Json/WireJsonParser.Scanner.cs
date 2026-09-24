namespace Weir.Core.Json;

/// <summary>Dispatches one JSON value by its first character; numbers, strings and containers scan their own bodies.</summary>
public static partial class WireJsonParser
{
    private static int SkipWhitespace(string s, int idx)
    {
        while (idx < s.Length && s[idx] is ' ' or '\t' or '\n' or '\r')
        {
            idx++;
        }

        return idx;
    }

    private static WireValue ScanOnceOrExpectingValue(string s, int idx, int depth, out int end)
    {
        var value = ScanOnce(s, idx, depth, out end);
        return value ?? throw new WireJsonDecodeException("Expecting value", idx);
    }

    /// <summary>Returns <see langword="null"/> when no value starts at <paramref name="idx"/>, so the caller chooses the error.</summary>
    private static WireValue? ScanOnce(string s, int idx, int depth, out int end)
    {
        end = idx;
        if (idx >= s.Length)
        {
            return null;
        }

        var c = s[idx];
        switch (c)
        {
            case '"':
                return new WireString(ScanString(s, idx + 1, out end));
            case '{':
                return ParseObject(s, idx + 1, EnterContainer(depth, idx), out end);
            case '[':
                return ParseArray(s, idx + 1, EnterContainer(depth, idx), out end);
            case 'n' when idx + 4 <= s.Length && string.CompareOrdinal(s, idx, "null", 0, 4) == 0:
                end = idx + 4;
                return WireNull.Instance;
            case 't' when idx + 4 <= s.Length && string.CompareOrdinal(s, idx, "true", 0, 4) == 0:
                end = idx + 4;
                return WireBool.True;
            case 'f' when idx + 5 <= s.Length && string.CompareOrdinal(s, idx, "false", 0, 5) == 0:
                end = idx + 5;
                return WireBool.False;
            case 'N' when idx + 3 <= s.Length && string.CompareOrdinal(s, idx, "NaN", 0, 3) == 0:
                end = idx + 3;
                return new WireNumber(double.NaN);
            case 'I' when idx + 8 <= s.Length && string.CompareOrdinal(s, idx, "Infinity", 0, 8) == 0:
                end = idx + 8;
                return new WireNumber(double.PositiveInfinity);
            case '-' when idx + 9 <= s.Length && string.CompareOrdinal(s, idx, "-Infinity", 0, 9) == 0:
                end = idx + 9;
                return new WireNumber(double.NegativeInfinity);
            default:
                return MatchNumber(s, idx, out end);
        }
    }

    private static int EnterContainer(int depth, int idx) =>
        depth < MaxDepth ? depth + 1 : throw new WireJsonDecodeException($"Nested more than {MaxDepth} levels deep", idx);
}
