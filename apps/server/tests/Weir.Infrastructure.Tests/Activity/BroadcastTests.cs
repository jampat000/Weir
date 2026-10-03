using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Activity;

/// <summary>The feed every open stream listens to: each subscriber hears what is published after it joined, and a slow one never holds up the rest.</summary>
public sealed class BroadcastTests
{
    private static async Task<T> NextAsync<T>(BroadcastSubscription<T> subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var item in subscription.ReadAllAsync(timeout.Token))
        {
            return item;
        }

        throw new InvalidOperationException("The subscription ended.");
    }

    [Fact]
    public async Task Every_subscriber_hears_what_is_published()
    {
        var feed = new Broadcast<string>(backlog: 4);
        using var first = feed.Subscribe();
        using var second = feed.Subscribe();

        feed.Publish("one");

        Assert.Equal(("one", "one"), (await NextAsync(first), await NextAsync(second)));
    }

    [Fact]
    public async Task A_subscriber_does_not_hear_what_was_published_before_it_joined()
    {
        var feed = new Broadcast<string>(backlog: 4);
        feed.Publish("early");
        using var subscription = feed.Subscribe();

        feed.Publish("late");

        Assert.Equal("late", await NextAsync(subscription));
    }

    [Fact]
    public async Task A_subscriber_that_falls_behind_loses_its_oldest_items_first()
    {
        var feed = new Broadcast<int>(backlog: 2);
        using var slow = feed.Subscribe();

        feed.Publish(1);
        feed.Publish(2);
        feed.Publish(3);

        Assert.Equal((2, 3), (await NextAsync(slow), await NextAsync(slow)));
    }

    [Fact]
    public async Task A_subscriber_that_has_left_hears_nothing_and_its_stream_ends()
    {
        var feed = new Broadcast<string>(backlog: 4);
        var leaver = feed.Subscribe();
        leaver.Dispose();

        feed.Publish("after");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Empty(await leaver.ReadAllAsync(timeout.Token).ToListAsync(timeout.Token));
    }

    [Fact]
    public void A_feed_must_hold_at_least_one_item_for_each_subscriber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Broadcast<string>(backlog: 0));
    }
}
