using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Scheduling;

/// <summary>
/// Background work that runs on a timer for as long as the server runs.
/// <see cref="PeriodicTaskService"/> hosts every registered task.
/// </summary>
public interface IPeriodicTask
{
    /// <summary>The task's name, used in its logger category.</summary>
    string Name { get; }

    /// <summary>Time between runs.</summary>
    TimeSpan Interval { get; }

    /// <summary>Run once as soon as the host starts, rather than after the first interval.</summary>
    bool RunAtStart { get; }

    /// <summary>How long to wait after a failed run before trying again (the interval when <see langword="null"/>).</summary>
    TimeSpan? FailureCooldown { get; }

    /// <summary>What is logged (with the exception) when a run fails.</summary>
    string FailureMessage { get; }

    Task RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One periodic loop: optionally wait an interval first, then run; after a success wait the interval,
/// after a failure log it and wait the cooldown (or the interval). Stops quietly on cancellation.
/// </summary>
public static class PeriodicTaskRunner
{
    public static async Task RunAsync(IPeriodicTask task, TimeProvider time, ILogger logger, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        if (!task.RunAtStart && !await DelayAsync(task.Interval, time, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                await task.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                wait = task.Interval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed run is logged and retried; the loop must survive it.
            catch (Exception exception)
#pragma warning restore CA1031
            {
#pragma warning disable CA2254 // The message is each task's fixed wording, not a template with arguments.
                logger.LogError(exception, task.FailureMessage);
#pragma warning restore CA2254
                wait = task.FailureCooldown ?? task.Interval;
            }

            if (!await DelayAsync(wait, time, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, TimeProvider time, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, time, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>Runs every registered <see cref="IPeriodicTask"/> on its own loop while the server runs.</summary>
public sealed class PeriodicTaskService : BackgroundService
{
    private readonly IReadOnlyList<IPeriodicTask> _tasks;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;

    public PeriodicTaskService(IEnumerable<IPeriodicTask> tasks, TimeProvider time, ILoggerFactory loggers)
    {
        _tasks = [.. tasks];
        _time = time;
        _loggers = loggers;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_tasks.Select(task => Task.Run(
            () => PeriodicTaskRunner.RunAsync(task, _time, _loggers.CreateLogger($"Weir.Periodic.{task.Name}"), stoppingToken),
            CancellationToken.None)));
}

/// <summary><c>auth-session-cleanup</c>: delete inactive sessions (ones that can never authenticate again), hourly.</summary>
public sealed class SessionCleanupTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly AuthService _auth;
    private readonly ILogger _logger;

    public SessionCleanupTask(SqliteDatabase database, AuthService auth, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _database = database;
        _auth = auth;
        _logger = loggerFactory.CreateLogger("weir.platform.auth.session_cleanup");
    }

    public string Name => "auth-session-cleanup";

    public TimeSpan Interval => TimeSpan.FromSeconds(3600);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "auth event: inactive session cleanup failed";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var removed = await _auth.CleanupInactiveSessionsAsync(uow).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
            if (removed > 0)
            {
                _logger.LogInformation("auth event: cleaned up inactive sessions (count={Count})", removed);
            }
            else
            {
                _logger.LogDebug("auth event: inactive session cleanup found nothing to remove");
            }
        }
    }
}

/// <summary><c>suite-log-retention</c>: checked hourly, prunes <c>weir.log</c> at most once a day.</summary>
public sealed class LogRetentionTask : IPeriodicTask
{
    public static readonly TimeSpan MinRunInterval = TimeSpan.FromHours(24);

    private readonly SqliteDatabase _database;
    private readonly WeirLogFile _logFile;
    private readonly TimeProvider _time;
    private readonly SuiteSettingsStore _suiteSettings;
    private DateTimeOffset? _lastPruneAt;

    public LogRetentionTask(SqliteDatabase database, WeirLogFile logFile, TimeProvider time, SuiteSettingsStore suiteSettings)
    {
        _database = database;
        _logFile = logFile;
        _time = time;
        _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));
    }

    public string Name => "suite-log-retention";

    public TimeSpan Interval => TimeSpan.FromSeconds(3600);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Log retention tick failed";

    public Task RunOnceAsync(CancellationToken cancellationToken) => TickAsync(null, cancellationToken);

    /// <summary>One retention check: 1 when the log was pruned, 0 when the last prune was under a day ago.</summary>
    public async Task<int> TickAsync(DateTimeOffset? now, CancellationToken cancellationToken)
    {
        var when = now ?? _time.GetUtcNow();
        if (_lastPruneAt is { } last && when - last < MinRunInterval)
        {
            return 0;
        }

        _logFile.Prune(await ReadKeepDaysAsync(_database, cancellationToken).ConfigureAwait(false));
        _lastPruneAt = when;
        return 1;
    }

    /// <summary>
    /// The number of days of log to keep, shared with the startup prune: the suite settings'
    /// <c>log_retention_days</c>, at least 1.
    /// </summary>
    public async Task<int> ReadKeepDaysAsync(SqliteDatabase database, CancellationToken cancellationToken = default)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var suite = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
            return (int)Math.Max(1, Math.Min(int.MaxValue, suite.LogRetentionDays));
        }
    }
}

/// <summary><c>suite-configuration-backup</c>: every minute, write a snapshot when one is due.</summary>
public sealed class ConfigurationBackupTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly ConfigurationBackups _backups;

    public ConfigurationBackupTask(SqliteDatabase database, ConfigurationBackups backups)
    {
        _database = database;
        _backups = backups;
    }

    public string Name => "suite-configuration-backup";

    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public bool RunAtStart => true;

    /// <summary>A failed run is retried after five seconds rather than the next minute.</summary>
    public TimeSpan? FailureCooldown => TimeSpan.FromSeconds(5);

    public string FailureMessage => "Suite configuration backup tick failed";

    public Task RunOnceAsync(CancellationToken cancellationToken) => _backups.RunTickAsync(_database, null, cancellationToken);
}
