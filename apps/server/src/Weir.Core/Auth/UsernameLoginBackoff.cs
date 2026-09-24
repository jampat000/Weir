namespace Weir.Core.Auth;

/// <summary>
/// Per-username login backoff, layered on top of the per-IP <see cref="SlidingWindowLimiter"/>: guessing at
/// one account gets <see cref="FreeAttempts"/> free tries regardless of how many source addresses it comes
/// from, then each further attempt has to wait, doubling up to <see cref="MaxBackoffSeconds"/>.
/// </summary>
public sealed class UsernameLoginBackoff
{
    public const int FreeAttempts = 5;
    public const int WindowSeconds = 15 * 60;
    public const int MaxBackoffSeconds = 15 * 60;

    /// <summary>Evicted like <see cref="SlidingWindowLimiter"/>'s buckets: least-recently-touched first.</summary>
    private const int MaxTrackedUsernames = 10_000;

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly long _startTimestamp;
    private readonly LinkedList<(string Key, Entry State)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Entry State)>> _entries = new(StringComparer.Ordinal);

    public UsernameLoginBackoff(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _startTimestamp = time.GetTimestamp();
    }

    /// <summary>How long <paramref name="username"/> must still wait, or <see cref="TimeSpan.Zero"/> when it may try now.</summary>
    public TimeSpan TimeUntilAllowed(string username)
    {
        var key = NormalizeKey(username);
        var now = ElapsedSeconds();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                return TimeSpan.Zero;
            }

            var state = node.Value.State;
            if (now - state.LastFailureSeconds > WindowSeconds)
            {
                Remove(node);
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds(Math.Max(0, state.LockedUntilSeconds - now));
        }
    }

    /// <summary>Records one failed sign-in and, past <see cref="FreeAttempts"/> in the window, extends the wait.</summary>
    public void RecordFailure(string username)
    {
        var key = NormalizeKey(username);
        var now = ElapsedSeconds();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var node) || now - node.Value.State.LastFailureSeconds > WindowSeconds)
            {
                if (node is not null)
                {
                    Remove(node);
                }

                node = _order.AddLast((key, new Entry()));
                _entries[key] = node;
            }

            var state = node.Value.State;
            state.Failures++;
            state.LastFailureSeconds = now;
            if (state.Failures > FreeAttempts)
            {
                state.LockedUntilSeconds = now + Math.Min(MaxBackoffSeconds, Math.Pow(2, state.Failures - FreeAttempts));
            }

            Touch(node);
            while (_entries.Count > MaxTrackedUsernames)
            {
                Remove(_order.First!);
            }
        }
    }

    /// <summary>A successful sign-in clears the account's history — a real owner regaining access should not still wait.</summary>
    public void RecordSuccess(string username)
    {
        var key = NormalizeKey(username);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                Remove(node);
            }
        }
    }

    private double ElapsedSeconds() => _time.GetElapsedTime(_startTimestamp).TotalSeconds;

    private static string NormalizeKey(string username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    private void Touch(LinkedListNode<(string Key, Entry State)> node)
    {
        _order.Remove(node);
        _order.AddLast(node);
    }

    private void Remove(LinkedListNode<(string Key, Entry State)> node)
    {
        _order.Remove(node);
        _entries.Remove(node.Value.Key);
    }

    private sealed class Entry
    {
        public int Failures;
        public double LastFailureSeconds;
        public double LockedUntilSeconds;
    }
}
