using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// Minimum spacing between outbound rejects, across the whole process (registered as a singleton so one lock and clock
/// are shared by every caller). The wait is asynchronous, so it never ties up a worker thread.
/// </summary>
public sealed class RejectPacing : IDisposable
{
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRejectAt = DateTimeOffset.MinValue;

    public RejectPacing(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = RejectSupportRules.MinSecondsBetweenRejects - (_time.GetUtcNow() - _lastRejectAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }

            _lastRejectAt = _time.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
