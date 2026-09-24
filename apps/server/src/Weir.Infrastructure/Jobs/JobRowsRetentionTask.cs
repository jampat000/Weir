using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// <c>platform-job-rows-retention</c>: prune on start, then every
/// <c>WEIR_JOB_ROWS_RETENTION_SCHEDULE_INTERVAL_SECONDS</c>.
/// </summary>
public sealed class JobRowsRetentionTask : IPeriodicTask
{
    private readonly JobRowsRetention _retention;
    private readonly WeirOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<JobRowsRetentionTask> _logger;

    public JobRowsRetentionTask(JobRowsRetention retention, WeirOptions options, TimeProvider time, ILogger<JobRowsRetentionTask> logger)
    {
        _retention = retention;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public string Name => "platform-job-rows-retention";

    public TimeSpan Interval => TimeSpan.FromSeconds(_options.JobRowsRetentionScheduleIntervalSeconds);

    public bool RunAtStart => true;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Job-row retention prune tick failed";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var counts = await Task.Run(
            () => _retention.RunTickAsync(_options.JobRowsRetentionDays, _time.GetUtcNow(), cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (counts.Total > 0)
        {
            _logger.LogInformation(
                "History retention pruned processing jobs={ProcessingJobs} activity events={ActivityEvents}",
                counts.Processing,
                counts.Activity);
        }
    }
}
