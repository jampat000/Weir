using Microsoft.Extensions.Time.Testing;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.ConnectionTraffic;

namespace Weir.Infrastructure.Tests.ConnectionTraffic;

/// <summary>What the Connections views are told: every phase, each connection throttled on its own, and the last phase never lost.</summary>
public sealed class ConnectionActivityHubTests
{
    private static readonly ConnectionRef Radarr = new(ConnectionKind.MediaManager, 1);
    private static readonly ConnectionRef Sonarr = new(ConnectionKind.MediaManager, 2);
    private static readonly ConnectionRef QBittorrent = new(ConnectionKind.DownloadClient, 1);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Everything the stream was sent so far; the stream is over afterwards, so look again with a new one.</summary>
    private static async Task<List<ConnectionActivity>> EverythingSentAsync(ConnectionActivitySubscription subscription)
    {
        subscription.Dispose();
        var sent = new List<ConnectionActivity>();
        await foreach (var activity in subscription.ReadAllAsync(CancellationToken.None))
        {
            sent.Add(activity);
        }

        return sent;
    }

    private static void Ask(ConnectionActivityHub hub, ConnectionRef connection) =>
        hub.Publish(connection, ConnectionPhase.Asked, ConnectionDirection.Outbound, milliseconds: null);

    private static void Answer(ConnectionActivityHub hub, ConnectionRef connection, long milliseconds) =>
        hub.Publish(connection, ConnectionPhase.Answered, ConnectionDirection.Outbound, milliseconds);

    [Fact]
    public async Task A_stream_hears_what_a_call_asked_and_how_it_was_answered()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();

        Ask(hub, Radarr);
        _time.Advance(TimeSpan.FromMilliseconds(84));
        Answer(hub, Radarr, 84);

        var sent = await EverythingSentAsync(subscription);
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered], sent.Select(activity => activity.Phase));
        Assert.All(sent, activity => Assert.Equal(Radarr, activity.Connection));
        Assert.Equal([null, 84L], sent.Select(activity => activity.Milliseconds));
        Assert.Equal(_time.GetUtcNow().UtcDateTime, sent[1].At.AsUtc);
    }

    [Fact]
    public async Task A_stream_opened_later_hears_nothing_from_before_it_opened()
    {
        var hub = new ConnectionActivityHub(_time);
        Ask(hub, Radarr);
        _time.Advance(ConnectionActivityHub.Throttle);

        var subscription = hub.Subscribe();
        Answer(hub, Radarr, 10);

        Assert.Equal([ConnectionPhase.Answered], (await EverythingSentAsync(subscription)).Select(activity => activity.Phase));
    }

    [Fact]
    public async Task A_second_frame_of_the_same_phase_waits_for_the_throttle_window_to_end_and_is_then_sent()
    {
        var hub = new ConnectionActivityHub(_time);
        Ask(hub, Radarr);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Ask(hub, Radarr);

        var beforeItEnds = hub.Subscribe();
        _time.Advance(TimeSpan.FromMilliseconds(149));
        var beforeItEndsHeard = await EverythingSentAsync(beforeItEnds);
        var atTheEnd = hub.Subscribe();
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Empty(beforeItEndsHeard);
        Assert.Single(await EverythingSentAsync(atTheEnd));
    }

    [Fact]
    public async Task Different_phases_of_one_connection_are_not_throttled_against_each_other()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();

        Ask(hub, Radarr);
        Answer(hub, Radarr, 5);
        hub.Publish(Radarr, ConnectionPhase.Failed, ConnectionDirection.Outbound, 5);

        Assert.Equal(
            [ConnectionPhase.Asked, ConnectionPhase.Answered, ConnectionPhase.Failed],
            (await EverythingSentAsync(subscription)).Select(activity => activity.Phase));
    }

    [Fact]
    public async Task Each_connection_is_throttled_on_its_own()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();

        Ask(hub, Radarr);
        Ask(hub, Sonarr);
        Ask(hub, QBittorrent);

        Assert.Equal([Radarr, Sonarr, QBittorrent], (await EverythingSentAsync(subscription)).Select(activity => activity.Connection));
    }

    [Fact]
    public async Task A_burst_is_cut_to_the_frames_that_matter_and_ends_on_the_phase_the_connection_ended_on()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();

        Ask(hub, Radarr);
        Answer(hub, Radarr, 20);
        Ask(hub, Radarr);
        Answer(hub, Radarr, 30);
        _time.Advance(ConnectionActivityHub.Throttle);

        var sent = await EverythingSentAsync(subscription);
        Assert.Equal([ConnectionPhase.Asked, ConnectionPhase.Answered, ConnectionPhase.Answered], sent.Select(activity => activity.Phase));
        Assert.Equal([null, 20L, 30L], sent.Select(activity => activity.Milliseconds));
    }

    [Fact]
    public async Task A_stream_that_has_ended_is_sent_nothing_more()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();
        Ask(hub, Radarr);
        var sent = await EverythingSentAsync(subscription);

        _time.Advance(ConnectionActivityHub.Throttle);
        Ask(hub, Radarr);

        Assert.Single(sent);
    }

    [Fact]
    public async Task Every_open_stream_is_sent_each_frame()
    {
        var hub = new ConnectionActivityHub(_time);
        var first = hub.Subscribe();
        var second = hub.Subscribe();

        Ask(hub, Radarr);

        Assert.Single(await EverythingSentAsync(first));
        Assert.Single(await EverythingSentAsync(second));
    }

    [Fact]
    public async Task A_stream_that_falls_behind_drops_its_oldest_frames_not_its_newest()
    {
        var hub = new ConnectionActivityHub(_time);
        var subscription = hub.Subscribe();

        for (var call = 0; call < 400; call++)
        {
            _time.Advance(ConnectionActivityHub.Throttle);
            Answer(hub, Radarr, call);
        }

        var sent = await EverythingSentAsync(subscription);
        Assert.True(sent.Count < 400);
        Assert.Equal(399, sent[^1].Milliseconds);
    }

    [Fact]
    public async Task Every_published_call_is_noted_in_the_usage_ledger_even_when_its_frame_is_held_back()
    {
        var ledger = new ConnectionUsageLedger();
        var hub = new ConnectionActivityHub(_time, ledger);
        var subscription = hub.Subscribe();

        Answer(hub, Radarr, 40);
        Answer(hub, Radarr, 55);
        await EverythingSentAsync(subscription);

        Assert.Equal(55, ledger.Overlay(Radarr, null, null).AnswerMilliseconds);
    }
}
