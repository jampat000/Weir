using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Activity;

/// <summary>Listens to a <see cref="DataChangePublisher"/> and reports what was published since it was last asked.</summary>
internal sealed class PublishedTopics : IDisposable
{
    private const string Mark = "end-of-batch";

    private readonly DataChangePublisher _publisher;
    private readonly BroadcastSubscription<string> _subscription;

    public PublishedTopics(DataChangePublisher publisher)
    {
        _publisher = publisher;
        _subscription = publisher.Subscribe();
    }

    /// <summary>Every topic published since the last call, in order. Publishing is synchronous, so a mark placed now ends the batch.</summary>
    public async Task<IReadOnlyList<string>> TakeAsync()
    {
        _publisher.Publish(Mark);
        var topics = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var topic in _subscription.ReadAllAsync(timeout.Token))
        {
            if (topic == Mark)
            {
                break;
            }

            topics.Add(topic);
        }

        return topics;
    }

    public void Dispose() => _subscription.Dispose();
}
