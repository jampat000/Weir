using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Scheduling;

namespace Weir.Api.Endpoints;

/// <summary>The list of scheduled tasks as <c>GET /api/v1/system/tasks</c> and the <c>system.tasks</c> frame both send it.</summary>
public static class SystemTasksWire
{
    public static WireArray List(IReadOnlyList<PeriodicTaskStatus> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new WireArray(tasks.Select(task => (WireValue)One(task)));
    }

    private static WireObject One(PeriodicTaskStatus task) => new WireObject()
        .Set("key", task.Key)
        .Set("label", task.Label)
        .Set("running", task.Running)
        .Set("last_run_at", WireTime(task.LastRunAt))
        .Set("last_ok", task.LastOk is { } ok ? WireValue.Of(ok) : WireValue.Null)
        .Set("last_error", task.LastError)
        .Set("next_run_at", WireTime(task.NextRunAt))
        .Set("interval_seconds", task.Interval is { } interval ? WireValue.Of((long)interval.TotalSeconds) : WireValue.Null);

    private static string? WireTime(DateTimeOffset? moment) =>
        moment is { } at ? Timestamp.FromDateTimeOffset(at.ToUniversalTime()).ToWireText() : null;
}
