using Weir.Infrastructure.Sqlite;

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

    /// <summary>
    /// Says that the data named by <paramref name="topic"/> changed once <paramref name="uow"/> commits; a rollback says nothing.
    /// Any number of calls for one topic in one unit of work say it once, so a sync writing many rows makes each open screen read once.
    /// </summary>
    public void PublishOnCommit(UnitOfWork uow, string topic)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (uow.Items.TryAdd($"data_changed:{topic}", true))
        {
            uow.OnCommitted(() => Publish(topic));
        }
    }

    /// <summary>Whether any stream is open, so work that only keeps open screens current can wait until one is.</summary>
    public bool HasListeners => _feed.HasSubscribers;

    /// <summary>Starts a stream of every topic published from now on. Dispose it when the stream ends.</summary>
    public BroadcastSubscription<string> Subscribe() => _feed.Subscribe();
}
