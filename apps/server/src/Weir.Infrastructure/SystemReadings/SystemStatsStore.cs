namespace Weir.Infrastructure.SystemReadings;

/// <summary>A reading and its place in the order of readings taken.</summary>
public sealed record StatsUpdate(long Version, StatsSample Sample);

/// <summary>
/// What the System view is built from, kept in memory for as long as Weir runs: a ring of the newest points, the latest
/// reading, the machine's slow facts and the drives. The sampler writes it; every open stream and request reads it, so
/// nothing reads the machine on a caller's behalf.
/// </summary>
public sealed class SystemStatsStore(TimeProvider time, int cores)
{
    /// <summary>The most points kept.</summary>
    public const int Capacity = 600;

    /// <summary>How far back the history reaches.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(Capacity);

    private readonly Lock _gate = new();
    private readonly Queue<StatsPoint> _points = new();
    private StatsSample? _latest;
    private long _version;
    private TaskCompletionSource _changed = NewSignal();
    private MachineFacts _machine = new(null, null, null);
    private IReadOnlyList<DriveReading> _drives = [];

    /// <summary>Adds a reading and wakes everything waiting for one.</summary>
    public void Add(StatsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        TaskCompletionSource woken;
        lock (_gate)
        {
            _points.Enqueue(sample.Point);
            while (_points.Count > Capacity)
            {
                _points.Dequeue();
            }

            _latest = sample;
            _version++;
            woken = _changed;
            _changed = NewSignal();
        }

        woken.TrySetResult();
    }

    public void SetMachine(MachineFacts machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        lock (_gate)
        {
            _machine = machine;
        }
    }

    public void SetDrives(IReadOnlyList<DriveReading> drives)
    {
        ArgumentNullException.ThrowIfNull(drives);
        lock (_gate)
        {
            _drives = drives;
        }
    }

    /// <summary>The newest reading, the points of the last <see cref="Window"/>, the machine's facts and the drives.</summary>
    public SystemStatsSnapshot Snapshot()
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            var from = now - Window;
            return new SystemStatsSnapshot(
                _latest?.Now ?? StatsNow.Unread(now, cores),
                [.. _points.Where(point => point.At > from)],
                _machine,
                _drives);
        }
    }

    /// <summary>The newest reading if it is newer than <paramref name="version"/> (0 asks for whatever there is), otherwise the next one to arrive.</summary>
    public async Task<StatsUpdate> NextAfterAsync(long version, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_latest is { } latest && _version != version)
                {
                    return new StatsUpdate(_version, latest);
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
