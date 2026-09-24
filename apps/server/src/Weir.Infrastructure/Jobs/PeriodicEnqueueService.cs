using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Runs every registered <see cref="IPeriodicEnqueuer"/> on its own timer: enqueue, then wait the interval;
/// after a failure, wait two seconds and try again.
/// </summary>
/// <remarks>
/// <para>A family is only timed when this server has a handler for its job kind, so the queue never fills with
/// work no worker here can run.</para>
/// <para>Each timer checks the family's switch and interval every <see cref="DefaultRecheck"/>, so a change in
/// Settings › Cleanup applies without a restart: a family switched on runs at once, one switched off stops, and a
/// new interval counts from its last run.</para>
/// </remarks>
public sealed class PeriodicEnqueueService : BackgroundService
{
    private readonly IReadOnlyList<IPeriodicEnqueuer> _enqueuers;
    private readonly JobHandlerRegistry _handlers;
    private readonly TimeProvider _time;
    private readonly ILogger<PeriodicEnqueueService> _logger;
    private readonly PeriodicEnqueueClock _clock;
    private readonly TimeSpan _recheck;
    private readonly Action? _onIterationComplete;

    /// <summary>How often a timer looks at its family's switch and interval again.</summary>
    public static readonly TimeSpan DefaultRecheck = TimeSpan.FromSeconds(30);

    public PeriodicEnqueueService(
        IEnumerable<IPeriodicEnqueuer> enqueuers,
        JobHandlerRegistry handlers,
        TimeProvider time,
        ILogger<PeriodicEnqueueService> logger,
        PeriodicEnqueueClock? clock = null,
        TimeSpan? recheck = null)
        : this(enqueuers, handlers, time, logger, clock, recheck, onIterationComplete: null)
    {
    }

    /// <summary>
    /// For tests: <paramref name="onIterationComplete"/> fires once per family's loop pass, after that pass's
    /// switch check and any enqueue it made, right before the loop waits again. A test driving a
    /// <c>FakeTimeProvider</c> can wait on it instead of a fixed real-time sleep to know a specific
    /// <c>Advance</c> call has actually been acted on, on a thread-pool schedule no test controls.
    /// </summary>
    internal PeriodicEnqueueService(
        IEnumerable<IPeriodicEnqueuer> enqueuers,
        JobHandlerRegistry handlers,
        TimeProvider time,
        ILogger<PeriodicEnqueueService> logger,
        PeriodicEnqueueClock? clock,
        TimeSpan? recheck,
        Action? onIterationComplete)
    {
        _enqueuers = [.. enqueuers];
        _handlers = handlers;
        _time = time;
        _logger = logger;
        _clock = clock ?? new PeriodicEnqueueClock();
        _recheck = recheck ?? DefaultRecheck;
        _onIterationComplete = onIterationComplete;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var running = new List<Task>();
        foreach (var enqueuer in _enqueuers)
        {
            if (!_handlers.Contains(enqueuer.JobKind))
            {
                continue;
            }

            running.Add(Task.Run(() => RunAsync(enqueuer, stoppingToken), CancellationToken.None));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>
    /// One family's timer: while switched on, enqueue when due (at once the first time), then every interval; two seconds
    /// after a failure. The switch and the interval are read again before every wait, and no wait is longer than the
    /// recheck, so a change in Settings › Cleanup applies without a restart.
    /// </summary>
    internal async Task RunAsync(IPeriodicEnqueuer enqueuer, CancellationToken stoppingToken)
    {
        DateTimeOffset? lastRun = null;
        DateTimeOffset? retryAt = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var (enabled, interval) = await ReadSwitchAsync(enqueuer, stoppingToken).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var wait = _recheck;
            if (!enabled || interval <= TimeSpan.Zero)
            {
                _clock.Forget(enqueuer.Name);
                retryAt = null;
            }
            else
            {
                var now = _time.GetUtcNow();
                var due = retryAt ?? (lastRun is { } last ? last + interval : now);
                if (due <= now)
                {
                    if (await TryEnqueueAsync(enqueuer, stoppingToken).ConfigureAwait(false))
                    {
                        lastRun = now;
                        retryAt = null;
                    }
                    else if (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    else
                    {
                        retryAt = now + PeriodicSchedule.FailureCooldown;
                    }

                    due = retryAt ?? now + interval;
                }

                _clock.Record(enqueuer.Name, enqueuer.JobKind, due, interval);
                var untilDue = due - _time.GetUtcNow();
                wait = untilDue >= _recheck ? _recheck : untilDue > TimeSpan.Zero ? untilDue : TimeSpan.Zero;
            }

            _onIterationComplete?.Invoke();

            try
            {
                await Task.Delay(wait, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<(bool Enabled, TimeSpan Interval)> ReadSwitchAsync(IPeriodicEnqueuer enqueuer, CancellationToken stoppingToken)
    {
        try
        {
            var enabled = await enqueuer.IsEnabledAsync(stoppingToken).ConfigureAwait(false);
            return (enabled, enabled ? await enqueuer.IntervalAsync(stoppingToken).ConfigureAwait(false) : TimeSpan.Zero);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return (false, TimeSpan.Zero);
        }
#pragma warning disable CA1031 // An unreadable switch leaves the family off until the next check; the loop must survive it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Could not read whether {Name} is enabled; leaving it off.", enqueuer.Name);
            return (false, TimeSpan.Zero);
        }
    }

    private async Task<bool> TryEnqueueAsync(IPeriodicEnqueuer enqueuer, CancellationToken stoppingToken)
    {
        try
        {
            await enqueuer.EnqueueOnceAsync(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
#pragma warning disable CA1031 // A failed run is logged and retried after the cooldown; the loop must survive it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Periodic enqueue failed ({Name})", enqueuer.Name);
            return false;
        }
    }
}
