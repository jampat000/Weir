namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Tells idle Processing worker slots that a job was queued, so it is picked up at once rather than at the next poll
/// (#716). Each slot waits on a signal of its own and still decides for itself whether to claim: this only wakes, so
/// nothing waits in line behind it, and the poll stays as the fallback for work that becomes due without being queued
/// (a retry's wait ending, processing being resumed).
/// </summary>
public sealed class WorkerWakeSignals
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, TaskCompletionSource> _waiting = [];

    /// <summary>
    /// Completes at the next <see cref="WakeAll"/>. A slot takes it before it looks for work, so a job queued while it is
    /// looking still wakes it.
    /// </summary>
    public Task NextWake(int slot)
    {
        lock (_lock)
        {
            if (!_waiting.TryGetValue(slot, out var signal))
            {
                signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[slot] = signal;
            }

            return signal.Task;
        }
    }

    public void WakeAll()
    {
        TaskCompletionSource[] woken;
        lock (_lock)
        {
            woken = [.. _waiting.Values];
            _waiting.Clear();
        }

        foreach (var signal in woken)
        {
            signal.TrySetResult();
        }
    }
}
