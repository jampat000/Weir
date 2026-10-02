using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Logging;

namespace Weir.Api.Endpoints;

/// <summary>
/// The <c>system.log</c> side of the Activity stream: one frame for each warning or error Weir writes to its log, so the Log
/// card gains a row the moment it happens. A stream gets nothing logged before it opened; <c>GET /suite/logs</c> has that.
/// </summary>
public sealed class SystemLogFrames
{
    private readonly LogAlerts _alerts;

    public SystemLogFrames(LogAlerts alerts) => _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));

    /// <summary>The frames for one open stream, from when it opens until it ends.</summary>
    public async IAsyncEnumerable<string> ForAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = _alerts.Subscribe();
        await foreach (var alert in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return Frame(alert);
        }
    }

    /// <summary>The <c>system.log</c> SSE frame for one warning or error.</summary>
    public static string Frame(LogAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        var data = new WireObject()
            .Set("at", Timestamp.FromDateTimeOffset(alert.At.ToUniversalTime()).ToWireText())
            .Set("level", alert.Level)
            .Set("message", alert.Message);
        return $"event: system.log\ndata: {WireJsonWriter.Dumps(data, WireJsonFormat.Compact)}\n\n";
    }
}
