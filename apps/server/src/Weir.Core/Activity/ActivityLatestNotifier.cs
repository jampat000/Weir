namespace Weir.Core.Activity;

/// <summary>The latest committed Activity id the notifier heard about, and how many notifications it has had.</summary>
public readonly record struct ActivityLatest(long? LatestId, long Version);

/// <summary>
/// Commit-time Activity freshness broadcaster for the live stream. Writers call <see cref="Notify"/>
/// after a transaction that recorded or updated Activity commits; each open stream waits for a change.
/// </summary>
public sealed class ActivityLatestNotifier
{
    private readonly Lock _lock = new();
    private readonly HashSet<TaskCompletionSource<ActivityLatest>> _waiters = [];
    private long? _latestId;
    private long _version;

    /// <summary>The current latest id and version.</summary>
    public ActivityLatest Snapshot()
    {
        lock (_lock)
        {
            return new ActivityLatest(_latestId, _version);
        }
    }

    /// <summary>Records <paramref name="latestId"/>, bumps the version and wakes every waiter.</summary>
    public void Notify(long latestId)
    {
        TaskCompletionSource<ActivityLatest>[] waiters;
        ActivityLatest latest;
        lock (_lock)
        {
            _latestId = latestId;
            _version++;
            latest = new ActivityLatest(_latestId, _version);
            waiters = [.. _waiters];
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult(latest);
        }
    }

    /// <summary>
    /// The current state at once when the version already differs from
    /// <paramref name="previousVersion"/>, otherwise the next notification, or <see langword="null"/> after
    /// <paramref name="timeout"/>.
    /// </summary>
    public async Task<ActivityLatest?> WaitForChangeAsync(long previousVersion, TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(time);
        var waiter = new TaskCompletionSource<ActivityLatest>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_version != previousVersion)
            {
                return new ActivityLatest(_latestId, _version);
            }

            _waiters.Add(waiter);
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout, time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            lock (_lock)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    /// <summary>How many streams are waiting; for tests.</summary>
    public int WaiterCount
    {
        get
        {
            lock (_lock)
            {
                return _waiters.Count;
            }
        }
    }
}
