namespace Weir.Core.Rules;

/// <summary>Lexicographic ordering of sort keys; a key that is a prefix of another sorts first.</summary>
public sealed class SortKeyComparer : IComparer<IReadOnlyList<long>>
{
    public static SortKeyComparer Instance { get; } = new();

    public int Compare(IReadOnlyList<long>? x, IReadOnlyList<long>? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        var length = Math.Min(x.Count, y.Count);
        for (var i = 0; i < length; i++)
        {
            var order = x[i].CompareTo(y[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return x.Count.CompareTo(y.Count);
    }
}
