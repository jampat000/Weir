namespace Weir.Infrastructure.Activity;

/// <summary>
/// Tells every open Activity stream that one kind of data changed, so the screens showing it read it again without polling.
/// Any component calls <see cref="Publish"/> with a <see cref="DataTopics"/> name once the change is committed; the stream
/// sends it on as a <c>data.changed</c> frame. Publishing never waits on a reader and holds nothing while no stream is open.
/// </summary>
public sealed class DataChangePublisher
{
    /// <summary>How many topics one slow stream may hold before its oldest are dropped.</summary>
    private const int SubscriberBacklog = 256;

    private readonly Broadcast<string> _feed = new(SubscriberBacklog);

    /// <summary>Says that the data named by <paramref name="topic"/> (one of <see cref="DataTopics"/>) changed.</summary>
    public void Publish(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        _feed.Publish(topic);
    }

    /// <summary>Starts a stream of every topic published from now on. Dispose it when the stream ends.</summary>
    public BroadcastSubscription<string> Subscribe() => _feed.Subscribe();
}
