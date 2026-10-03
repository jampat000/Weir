namespace Weir.Core.Paging;

/// <summary>Which way a sorted list runs: smallest first, or largest first.</summary>
public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>The wire names of <see cref="SortDirection"/>, as a <c>direction</c> query parameter spells them.</summary>
public static class SortDirections
{
    public const string Ascending = "asc";
    public const string Descending = "desc";

    public static readonly IReadOnlyList<string> All = [Ascending, Descending];

    public static string NameOf(SortDirection direction) => direction == SortDirection.Ascending ? Ascending : Descending;

    /// <summary>The direction a wire name stands for, or false when it is neither of <see cref="All"/>.</summary>
    public static bool TryParse(string? name, out SortDirection direction)
    {
        direction = name == Ascending ? SortDirection.Ascending : SortDirection.Descending;
        return name is Ascending or Descending;
    }
}
