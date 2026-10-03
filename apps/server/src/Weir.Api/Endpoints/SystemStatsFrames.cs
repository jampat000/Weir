using Weir.Core.Json;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Api.Endpoints;

/// <summary>
/// The <c>system.stats</c> side of the Activity stream: each reading the sampler takes goes out as one frame, so the System
/// view's traces move once a second while a browser is watching. A stream gets the newest reading as soon as it opens.
/// Pacing is the sampler's, which reads once a second while any stream is open; nothing here polls.
/// </summary>
public sealed class SystemStatsFrames
{
    private readonly SystemStatsStore _store;

    public SystemStatsFrames(SystemStatsStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The frames for one open stream, from when it opens until it ends.</summary>
    public async IAsyncEnumerable<string> ForAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long seen = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var update = await _store.NextAfterAsync(seen, cancellationToken).ConfigureAwait(false);
            seen = update.Version;
            yield return Frame(update.Sample);
        }
    }

    /// <summary>The <c>system.stats</c> SSE frame: the readings of the moment and the point to add to the traces.</summary>
    public static string Frame(StatsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var data = new WireObject()
            .Set("now", SystemStatsWire.Now(sample.Now))
            .Set("point", SystemStatsWire.Point(sample.Point));
        return $"event: system.stats\ndata: {WireJsonWriter.Dumps(data, WireJsonFormat.Compact)}\n\n";
    }
}
