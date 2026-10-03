using System.Text;

namespace Weir.Core.Paging;

/// <summary>
/// Orders the rows of a list that is paged by position. A row's place is its <em>key</em>: the values of each part of the
/// sort, ending in one that is unique to the row, so two rows never share a place and a page can start right after any row.
/// </summary>
public static class KeysetOrder
{
    /// <summary>Negative when <paramref name="first"/> comes before <paramref name="second"/> in the order the parts give, positive when after.</summary>
    public static int Compare(IReadOnlyList<object?> first, IReadOnlyList<object?> second, IReadOnlyList<KeysetPart> parts)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(parts);
        for (var index = 0; index < parts.Count; index++)
        {
            var byValue = CompareValues(first[index], second[index], parts[index].Kind);
            if (byValue != 0)
            {
                return parts[index].Direction == SortDirection.Descending ? -byValue : byValue;
            }
        }

        return 0;
    }

    private static int CompareValues(object? first, object? second, KeysetValueKind kind)
    {
        if (first is null || second is null)
        {
            return (first is null ? 0 : 1) - (second is null ? 0 : 1);
        }

        return kind switch
        {
            KeysetValueKind.Number => ((long)first).CompareTo((long)second),
            KeysetValueKind.TextIgnoringCase => CompareBytes(Utf8LowercasingAscii((string)first), Utf8LowercasingAscii((string)second)),
            _ => CompareBytes(Encoding.UTF8.GetBytes((string)first), Encoding.UTF8.GetBytes((string)second)),
        };
    }

    private static int CompareBytes(byte[] first, byte[] second) => first.AsSpan().SequenceCompareTo(second);

    private static byte[] Utf8LowercasingAscii(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] is >= (byte)'A' and <= (byte)'Z')
            {
                bytes[index] += (byte)('a' - 'A');
            }
        }

        return bytes;
    }
}
