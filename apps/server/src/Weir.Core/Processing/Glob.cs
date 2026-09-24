namespace Weir.Core.Processing;

/// <summary>Minimal POSIX shell-glob matcher (<c>*</c>, <c>?</c>, <c>[seq]</c>/<c>[!seq]</c>), the subset
/// library include/exclude patterns need.</summary>
internal static class Glob
{
    public static bool IsMatch(string value, string pattern) => IsMatch(value, 0, pattern, 0);

    private static bool IsMatch(string value, int vi, string pattern, int pi)
    {
        while (pi < pattern.Length)
        {
            var pc = pattern[pi];
            if (pc == '*')
            {
                // Collapse consecutive '*' and try every split point.
                while (pi < pattern.Length && pattern[pi] == '*')
                {
                    pi++;
                }

                if (pi == pattern.Length)
                {
                    return true;
                }

                for (var k = vi; k <= value.Length; k++)
                {
                    if (IsMatch(value, k, pattern, pi))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (vi >= value.Length)
            {
                return false;
            }

            if (pc == '?')
            {
                vi++;
                pi++;
                continue;
            }

            if (pc == '[')
            {
                var close = pattern.IndexOf(']', pi + 1);
                if (close < 0)
                {
                    // Unterminated bracket: '[' is matched literally, as shell globs do.
                    if (value[vi] != '[')
                    {
                        return false;
                    }

                    vi++;
                    pi++;
                    continue;
                }

                var negate = pattern[pi + 1] is '!' or '^';
                var setStart = negate ? pi + 2 : pi + 1;
                var matched = false;
                for (var k = setStart; k < close; k++)
                {
                    if (k + 2 < close && pattern[k + 1] == '-')
                    {
                        if (value[vi] >= pattern[k] && value[vi] <= pattern[k + 2])
                        {
                            matched = true;
                        }

                        k += 2;
                    }
                    else if (pattern[k] == value[vi])
                    {
                        matched = true;
                    }
                }

                if (matched == negate)
                {
                    return false;
                }

                vi++;
                pi = close + 1;
                continue;
            }

            if (pattern[pi] != value[vi])
            {
                return false;
            }

            vi++;
            pi++;
        }

        return vi == value.Length;
    }
}
