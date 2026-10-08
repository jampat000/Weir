namespace Weir.Infrastructure.Activity;

/// <summary>
/// Says one kind of data changed, but no more often than <c>interval</c> allows, for work that changes it many times a second
/// (a scan writing its index a chunk at a time). The first call in a quiet spell goes out at once; calls inside the window
/// after it are dropped, so whoever ends the work publishes once more to cover the last of them.
/// </summary>
public sealed class ThrottledDataChange
{
    private readonly DataChangePublisher? _changes;
    private readonly string _topic;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private DateTimeOffset? _lastPublishedAt;

    /// <param name="changes">Where the topic is published; null publishes nothing (a host with no live stream).</param>
    /// <param name="topic">One of <see cref="DataTopics"/>.</param>
    /// <param name="interval">The shortest time between two publishes.</param>
    /// <param name="time">The clock the interval is measured on.</param>
    public ThrottledDataChange(DataChangePublisher? changes, string topic, TimeSpan interval, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(time);
        _changes = changes;
        _topic = topic;
        _interval = interval;
        _time = time;
    }

    /// <summary>Publishes the topic unless it already went out within the interval.</summary>
    public void Publish()
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_lastPublishedAt is { } last && now - last < _interval)
            {
                return;
            }

            _lastPublishedAt = now;
        }

        _changes?.Publish(_topic);
    }
}
