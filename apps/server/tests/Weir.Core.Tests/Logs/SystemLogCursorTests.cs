using Weir.Core.Logs;

namespace Weir.Core.Tests.Logs;

/// <summary>The cursor a page of System › Logs hands to the next.</summary>
public sealed class SystemLogCursorTests
{
    [Fact]
    public void A_cursor_comes_back_as_the_position_it_was_made_from()
    {
        var position = new SystemLogPosition(new DateTimeOffset(2026, 10, 2, 12, 30, 15, TimeSpan.Zero).AddTicks(1230), SystemLogSource.Job, 4121);

        var decoded = SystemLogCursor.TryDecode(SystemLogCursor.Encode(position), out var back);

        Assert.True(decoded);
        Assert.Equal(position, back);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("MTIzLjQuNQ")]
    [InlineData("LTEuMS4x")]
    public void Text_this_log_did_not_write_is_not_a_cursor(string? text)
    {
        Assert.False(SystemLogCursor.TryDecode(text, out _));
    }

    [Fact]
    public void A_cursor_is_safe_to_put_in_an_address()
    {
        var cursor = SystemLogCursor.Encode(new SystemLogPosition(DateTimeOffset.UnixEpoch.AddDays(20000), SystemLogSource.Server, 99));

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor);
    }

    [Fact]
    public void Newer_positions_come_first_then_the_later_source_then_the_higher_number()
    {
        var at = DateTimeOffset.UnixEpoch.AddDays(1);
        var newest = new SystemLogPosition(at.AddSeconds(1), SystemLogSource.Server, 1);
        var sameTimeEvent = new SystemLogPosition(at, SystemLogSource.Event, 1);
        var sameTimeJobHigh = new SystemLogPosition(at, SystemLogSource.Job, 9);
        var sameTimeJobLow = new SystemLogPosition(at, SystemLogSource.Job, 2);

        var ordered = new[] { sameTimeJobLow, newest, sameTimeEvent, sameTimeJobHigh }.Order(SystemLogPosition.NewestFirst).ToList();

        Assert.Equal([newest, sameTimeEvent, sameTimeJobHigh, sameTimeJobLow], ordered);
        Assert.True(sameTimeJobLow.IsAfter(sameTimeJobHigh));
        Assert.False(sameTimeJobHigh.IsAfter(sameTimeJobHigh));
    }
}
