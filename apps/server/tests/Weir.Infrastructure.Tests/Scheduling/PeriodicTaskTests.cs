using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Tests.Scheduling;

/// <summary>The periodic background task loop and its host.</summary>
public sealed class PeriodicTaskTests
{
    [Fact]
    public async Task A_task_that_runs_at_start_runs_straight_away_then_waits_the_interval()
    {
        var time = new FakeTimeProvider();
        var task = new CountingTask(runAtStart: true, interval: TimeSpan.FromHours(1));
        using var stop = new CancellationTokenSource();
        var loop = PeriodicTaskRunner.RunAsync(task, time, NullLogger.Instance, stop.Token);

        await Eventually.ThatAsync(() => task.Calls >= 1);
        // The loop is now parked in Task.Delay(Interval, time, ...): with a frozen fake clock and nothing
        // advancing it, a second call inside the interval is impossible rather than merely unobserved.
        Assert.Equal(1, task.Calls);

        await stop.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_task_that_does_not_run_at_start_waits_one_interval_first()
    {
        // auth-session-cleanup: the first cleanup is an interval after start; startup has just cleaned up.
        // A fake clock makes this deterministic (real wall-clock timing races under load, #557):
        // PeriodicTaskRunner's very first statement awaits Task.Delay(task.Interval, time, ...), whose timer
        // is registered on `time` synchronously before that await yields, so by the time RunAsync returns
        // the task to us the timer already exists and advancing the fake clock is race-free.
        var time = new FakeTimeProvider();
        var task = new CountingTask(runAtStart: false, interval: TimeSpan.FromMinutes(10));
        using var stop = new CancellationTokenSource();
        var loop = PeriodicTaskRunner.RunAsync(task, time, NullLogger.Instance, stop.Token);

        Assert.Equal(0, task.Calls);

        time.Advance(task.Interval);
        await Eventually.ThatAsync(() => task.Calls >= 1);

        await stop.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_failed_run_is_logged_and_retried_after_the_cooldown()
    {
        var time = new FakeTimeProvider();
        var cooldown = TimeSpan.FromSeconds(20);
        var task = new CountingTask(runAtStart: true, interval: TimeSpan.FromHours(1), cooldown: cooldown, failFirst: true);
        var logger = new RecordingLogger<PeriodicTaskTests>();
        using var stop = new CancellationTokenSource();
        var loop = PeriodicTaskRunner.RunAsync(task, time, logger, stop.Token);

        await Eventually.ThatAsync(() => logger.Errors.Count >= 1);
        Assert.Equal(["counting tick failed"], logger.Errors);
        Assert.Equal(1, task.Calls);

        time.Advance(cooldown);
        await Eventually.ThatAsync(() => task.Calls >= 2);

        await stop.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_host_runs_every_registered_task_once_however_often_it_is_added()
    {
        var first = new CountingTask(runAtStart: true, interval: TimeSpan.FromHours(1));
        var second = new CountingTask(runAtStart: true, interval: TimeSpan.FromHours(1));
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddSingleton<IPeriodicTask>(first)
            .AddSingleton<IPeriodicTask>(second);
        services.AddWeirPeriodicTasks();
        services.AddWeirPeriodicTasks();
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());

        await hosted.StartAsync(CancellationToken.None);
        await Eventually.ThatAsync(() => first.Calls >= 1 && second.Calls >= 1);
        await hosted.StopAsync(CancellationToken.None);
    }

    private sealed class CountingTask(bool runAtStart, TimeSpan interval, TimeSpan? cooldown = null, bool failFirst = false) : IPeriodicTask
    {
        private int _calls;

        public int Calls => _calls;

        public string Name => "counting";

        public TimeSpan Interval => interval;

        public bool RunAtStart => runAtStart;

        public TimeSpan? FailureCooldown => cooldown;

        public string FailureMessage => "counting tick failed";

        public Task RunOnceAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            return failFirst && call == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }
}
