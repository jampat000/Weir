using Weir.Core.Text;

namespace Weir.Core.Workers;

/// <summary>One lane's worker health as readiness reports it.</summary>
public sealed record WorkerLaneHealth(
    string Module,
    int ExpectedWorkers,
    int ActiveWorkers,
    int StaleWorkers,
    int StoppedWorkers,
    string Status,
    string Detail);

/// <summary>
/// In-memory worker heartbeat registry. An expected worker slot that has never reported reads as not
/// responding, so workers that fail to start show up as a degraded lane.
/// </summary>
public sealed class WorkerHeartbeats
{
    /// <summary>A running worker that has not reported for this long counts as stale.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(360);

    /// <summary>
    /// Display names for module keys whose plain <see cref="TitleCase"/> would not read as a person
    /// expects. The "processing" module key stays as it is (it is a stored identifier other modules and the
    /// web app match on), but operators know the app that runs it as Weir.
    /// </summary>
    private static readonly Dictionary<string, string> ModuleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["processing"] = "Weir",
    };

    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Module, int Index), Heartbeat> _heartbeats = [];

    public WorkerHeartbeats(TimeProvider time)
    {
        _time = time;
    }

    public void Started(string module, int index) => Record(module, index, running: true, resetStart: true);

    public void Beat(string module, int index) => Record(module, index, running: true, resetStart: false);

    public void Stopped(string module, int index) => Record(module, index, running: false, resetStart: false);

    public IReadOnlyList<WorkerLaneHealth> Snapshot(IReadOnlyList<KeyValuePair<string, int>> expectedWorkers)
    {
        ArgumentNullException.ThrowIfNull(expectedWorkers);
        var now = _time.GetTimestamp();
        List<(string Module, int Index, Heartbeat Beat)> rows;
        lock (_lock)
        {
            rows = [.. _heartbeats.Select(pair => (pair.Key.Module, pair.Key.Index, pair.Value))];
        }

        var lanes = new List<WorkerLaneHealth>(expectedWorkers.Count);
        foreach (var (module, expectedRaw) in expectedWorkers)
        {
            var expected = Math.Max(0, expectedRaw);
            var title = ModuleDisplayNames.TryGetValue(module, out var displayName) ? displayName : TitleCase(module);
            if (expected == 0)
            {
                lanes.Add(new WorkerLaneHealth(
                    module, 0, 0, 0, 0, "disabled",
                    $"{title} is turned off in Settings, so no new background work will run."));
                continue;
            }

            var moduleRows = rows.Where(row => row.Module == module && row.Index < expected).ToList();
            var active = moduleRows.Count(row => row.Beat.Running && _time.GetElapsedTime(row.Beat.LastSeen, now) <= StaleAfter);
            var stale = moduleRows.Count(row => row.Beat.Running && _time.GetElapsedTime(row.Beat.LastSeen, now) > StaleAfter);
            var stopped = moduleRows.Count(row => !row.Beat.Running);
            var missing = Math.Max(0, expected - moduleRows.Count);
            var degraded = stale + stopped + missing;
            lanes.Add(degraded > 0
                ? new WorkerLaneHealth(
                    module, expected, active, stale + missing, stopped, "degraded",
                    $"{title} is not processing new work because {Plural.Of(degraded, "worker slot")} stopped responding. " +
                    "Restart Weir; queued work remains safe.")
                : new WorkerLaneHealth(
                    module, expected, active, stale + missing, stopped, "healthy",
                    $"{title} worker heartbeats are current."));
        }

        return lanes;
    }

    /// <summary>Title case for module names: each letter that follows a non-letter is upper-cased, every other letter lower-cased.</summary>
    internal static string TitleCase(string value)
    {
        var chars = value.ToCharArray();
        var previousIsLetter = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var isLetter = char.IsLetter(chars[i]);
            chars[i] = isLetter && !previousIsLetter ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
            previousIsLetter = isLetter;
        }

        return new string(chars);
    }

    private void Record(string module, int index, bool running, bool resetStart)
    {
        var now = _time.GetTimestamp();
        lock (_lock)
        {
            _heartbeats[(module, index)] = _heartbeats.TryGetValue((module, index), out var existing) && !resetStart
                ? existing with { LastSeen = now, Running = running }
                : new Heartbeat(now, now, running);
        }
    }

    private sealed record Heartbeat(long StartedAt, long LastSeen, bool Running);
}
