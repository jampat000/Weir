using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Logging;

/// <summary>A line that was just written to the log.</summary>
/// <param name="At">When it was logged.</param>
/// <param name="Level">The level as the log file names it: <c>INFO</c>, <c>WARNING</c>, <c>ERROR</c> or <c>CRITICAL</c>.</param>
/// <param name="Message">The message the log line carries.</param>
public sealed record LogAlert(DateTimeOffset At, string Level, string Message);

/// <summary>
/// Every line the System screen's log shows, as it is logged, for the open streams: warnings and errors, and the information
/// from Weir's own loggers. The same lines <c>GET /suite/logs</c> leaves out as noise are left out here.
/// </summary>
public sealed class LogAlerts
{
    private const int SubscriberBacklog = 64;

    private readonly Broadcast<LogAlert> _feed = new(SubscriberBacklog);

    public void Publish(LogAlert alert) => _feed.Publish(alert);

    public BroadcastSubscription<LogAlert> Subscribe() => _feed.Subscribe();
}
