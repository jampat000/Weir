using Weir.Core.Paging;

namespace Weir.Core.Tests.Paging;

/// <summary>How the keys of rows in a sorted, paged list compare.</summary>
public sealed class KeysetOrderTests
{
    private static KeysetPart[] Parts(KeysetValueKind kind, SortDirection direction) => [new(kind, direction), new(KeysetValueKind.Number, direction)];

    [Fact]
    public void Numbers_compare_by_size_not_by_their_digits()
    {
        var parts = Parts(KeysetValueKind.Number, SortDirection.Ascending);

        Assert.True(KeysetOrder.Compare([9L, 1L], [10L, 1L], parts) < 0);
    }

    [Fact]
    public void A_later_part_breaks_a_tie_in_an_earlier_one()
    {
        var parts = Parts(KeysetValueKind.Text, SortDirection.Ascending);

        Assert.True(KeysetOrder.Compare(["a", 1L], ["a", 2L], parts) < 0);
        Assert.Equal(0, KeysetOrder.Compare(["a", 2L], ["a", 2L], parts));
    }

    [Fact]
    public void Descending_reverses_every_part()
    {
        var parts = Parts(KeysetValueKind.Text, SortDirection.Descending);

        Assert.True(KeysetOrder.Compare(["b", 1L], ["a", 9L], parts) < 0);
        Assert.True(KeysetOrder.Compare(["a", 9L], ["a", 1L], parts) < 0);
    }

    [Fact]
    public void Text_compares_capitals_before_lowercase_unless_it_ignores_case()
    {
        Assert.True(KeysetOrder.Compare(["Zeta", 1L], ["alpha", 1L], Parts(KeysetValueKind.Text, SortDirection.Ascending)) < 0);
        Assert.True(KeysetOrder.Compare(["Zeta", 1L], ["alpha", 1L], Parts(KeysetValueKind.TextIgnoringCase, SortDirection.Ascending)) > 0);
        Assert.Equal(0, KeysetOrder.Compare(["ALPHA", 1L], ["alpha", 1L], Parts(KeysetValueKind.TextIgnoringCase, SortDirection.Ascending)));
    }

    [Fact]
    public void Ignoring_case_folds_only_the_letters_a_to_z()
    {
        var parts = Parts(KeysetValueKind.TextIgnoringCase, SortDirection.Ascending);

        Assert.True(KeysetOrder.Compare(["É", 1L], ["é", 1L], parts) < 0);
    }

    [Fact]
    public void A_missing_value_comes_before_every_value_when_ascending_and_after_when_descending()
    {
        Assert.True(KeysetOrder.Compare([null, 1L], ["a", 1L], Parts(KeysetValueKind.Text, SortDirection.Ascending)) < 0);
        Assert.True(KeysetOrder.Compare([null, 1L], ["a", 1L], Parts(KeysetValueKind.Text, SortDirection.Descending)) > 0);
        Assert.Equal(0, KeysetOrder.Compare([null, 1L], [null, 1L], Parts(KeysetValueKind.Text, SortDirection.Ascending)));
    }
}
