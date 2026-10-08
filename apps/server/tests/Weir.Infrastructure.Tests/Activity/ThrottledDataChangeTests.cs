using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Activity;

/// <summary>Work that changes data many times a second says so at most once an interval.</summary>
public sealed class ThrottledDataChangeTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly DataChangePublisher _changes = new();

    private static async Task<string> NextAsync(BroadcastSubscription<string> subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var topic in subscription.ReadAllAsync(timeout.Token))
        {
            return topic;
        }

        throw new InvalidOperationException("The subscription ended.");
    }

    [Fact]
    public async Task The_first_call_goes_out_at_once_and_the_ones_inside_the_interval_do_not()
    {
        var throttle = new ThrottledDataChange(_changes, DataTopics.LibraryScan, Second, _time);
        using var subscription = _changes.Subscribe();

        throttle.Publish();
        _time.Advance(TimeSpan.FromMilliseconds(400));
        throttle.Publish();
        throttle.Publish();
        _changes.Publish(DataTopics.Settings);

        Assert.Equal(DataTopics.LibraryScan, await NextAsync(subscription));
        Assert.Equal(DataTopics.Settings, await NextAsync(subscription));
    }

    [Fact]
    public async Task A_call_after_the_interval_goes_out_again()
    {
        var throttle = new ThrottledDataChange(_changes, DataTopics.LibraryScan, Second, _time);
        using var subscription = _changes.Subscribe();
        throttle.Publish();

        _time.Advance(Second);
        throttle.Publish();

        Assert.Equal([DataTopics.LibraryScan, DataTopics.LibraryScan], [await NextAsync(subscription), await NextAsync(subscription)]);
    }

    [Fact]
    public void With_no_publisher_it_says_nothing_and_does_not_fail()
    {
        new ThrottledDataChange(null, DataTopics.LibraryScan, Second, _time).Publish();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_topic_must_be_named(string? topic)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ThrottledDataChange(_changes, topic!, Second, _time));
    }
}
