using System.Buffers.Text;
using System.Text;
using Weir.Core.Paging;

namespace Weir.Core.Tests.Paging;

/// <summary>The cursor a sorted, paged list hands to its next page.</summary>
public sealed class KeysetCursorTests
{
    private static readonly KeysetPart[] Shape =
    [
        new(KeysetValueKind.Text, SortDirection.Descending),
        new(KeysetValueKind.Number, SortDirection.Descending),
    ];

    [Fact]
    public void A_cursor_comes_back_as_the_key_it_was_made_from()
    {
        var cursor = KeysetCursor.Encode("when", SortDirection.Descending, ["2026-10-02 12:00:00", 4121L]);

        Assert.True(KeysetCursor.TryDecode(cursor, "when", SortDirection.Descending, Shape, out var key));
        Assert.Equal(["2026-10-02 12:00:00", 4121L], key);
    }

    [Fact]
    public void A_key_value_that_is_missing_or_not_plain_ascii_comes_back_unchanged()
    {
        var missing = KeysetCursor.Encode("file", SortDirection.Ascending, [null, long.MaxValue]);
        var accented = KeysetCursor.Encode("file", SortDirection.Ascending, ["Amélie \"2001\"/日本", 7L]);

        Assert.True(KeysetCursor.TryDecode(missing, "file", SortDirection.Ascending, Shape, out var missingKey));
        Assert.Equal([null, long.MaxValue], missingKey);
        Assert.True(KeysetCursor.TryDecode(accented, "file", SortDirection.Ascending, Shape, out var accentedKey));
        Assert.Equal(["Amélie \"2001\"/日本", 7L], accentedKey);
    }

    [Fact]
    public void A_cursor_is_safe_to_put_in_an_address()
    {
        var cursor = KeysetCursor.Encode("file", SortDirection.Ascending, ["a/b?c=d&e", 1L]);

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("e30")]
    [InlineData("W10")]
    [InlineData("eyJzb3J0Ijoid2hlbiJ9")]
    public void Text_this_server_did_not_write_is_not_a_cursor(string? text)
    {
        Assert.False(KeysetCursor.TryDecode(text, "when", SortDirection.Descending, Shape, out _));
    }

    [Fact]
    public void A_cursor_made_for_another_sort_or_direction_is_not_accepted()
    {
        var cursor = KeysetCursor.Encode("when", SortDirection.Descending, ["x", 1L]);

        Assert.False(KeysetCursor.TryDecode(cursor, "file", SortDirection.Descending, Shape, out _));
        Assert.False(KeysetCursor.TryDecode(cursor, "when", SortDirection.Ascending, Shape, out _));
    }

    [Fact]
    public void A_cursor_whose_values_do_not_fit_the_key_is_not_accepted()
    {
        var tooShort = KeysetCursor.Encode("when", SortDirection.Descending, ["x"]);
        var wrongType = KeysetCursor.Encode("when", SortDirection.Descending, [5L, "x"]);

        Assert.False(KeysetCursor.TryDecode(tooShort, "when", SortDirection.Descending, Shape, out _));
        Assert.False(KeysetCursor.TryDecode(wrongType, "when", SortDirection.Descending, Shape, out _));
    }

    [Fact]
    public void A_cursor_longer_than_any_this_server_writes_is_not_accepted()
    {
        var cursor = KeysetCursor.Encode("when", SortDirection.Descending, [new string('x', 4000), 1L]);

        Assert.False(KeysetCursor.TryDecode(cursor, "when", SortDirection.Descending, Shape, out _));
    }

    [Theory]
    [InlineData("{\"sort\":\"when\",\"direction\":\"desc\",\"after\":[\"x\",1.5]}")]
    [InlineData("{\"sort\":\"when\",\"direction\":\"desc\",\"after\":[\"x\",99999999999999999999]}")]
    [InlineData("{\"sort\":\"when\",\"direction\":\"desc\",\"after\":\"x\"}")]
    [InlineData("{\"sort\":\"when\",\"direction\":\"desc\",\"after\":[true,1]}")]
    [InlineData("{\"sort\":5,\"direction\":\"desc\",\"after\":[\"x\",1]}")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public void A_cursor_that_is_not_the_shape_this_server_writes_is_not_accepted(string json)
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        Assert.False(KeysetCursor.TryDecode(cursor, "when", SortDirection.Descending, Shape, out _));
    }

    [Fact]
    public void A_cursor_nested_deeper_than_json_allows_is_not_accepted()
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(new string('[', 1500)));

        Assert.False(KeysetCursor.TryDecode(cursor, "when", SortDirection.Descending, Shape, out _));
    }

    [Fact]
    public void A_cursor_that_is_not_text_is_not_accepted()
    {
        var cursor = Base64Url.EncodeToString([0xFF, 0xFE, 0x00, 0x7B]);

        Assert.False(KeysetCursor.TryDecode(cursor, "when", SortDirection.Descending, Shape, out _));
    }
}
