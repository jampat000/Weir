using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Jobs;

/// <summary>Registers the durable job queue, crash recovery, workers, retention and periodic enqueue.</summary>
public static class WeirJobs
{
    /// <summary>
    /// Adds the job queue and its services. Job handlers are added separately as <see cref="IJobHandler"/>
    /// singletons by the areas that own them; with none registered, the workers only refuse retired rows.
    /// </summary>
    public static IServiceCollection AddWeirJobs(this IServiceCollection services, WeirOptions options, RuntimeEnvironment runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);
        services.AddWeirPlatform(options);
        services.TryAddSingleton<IJobQueueMetrics>(NoJobQueueMetrics.Instance);
        services.TryAddSingleton<IJobNotifications, NoJobNotifications>();
        services.TryAddSingleton<IUnhandledJobFailureRecorder, NoUnhandledJobFailureRecorder>();
        services.TryAddSingleton(new WorkerLoopTimings { LeaseSeconds = options.ProcessingJobLeaseSeconds });
        services.AddWeirJobStore();
        services.TryAddSingleton(sp => new JobHandlerRegistry(sp.GetServices<IJobHandler>()));
        services.TryAddSingleton<ProcessingJobProcessor>();
        // The work file sweep is queued below, so it needs a handler or its jobs would wait in the queue for ever.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WorkTempStaleSweepHandler>());

        // Kill switches: an explicitly set variable that reads as off wins over the saved setting, for its own media type only.
        services.AddSingleton<IPeriodicEnqueuer>(sp => new WorkTempStaleSweepEnqueuer(
            sp.GetRequiredService<ProcessingJobStore>(), "movie", TimeSpan.FromSeconds(options.ProcessingWorkTempStaleSweepMovieScheduleIntervalSeconds),
            SwitchedOffByEnvironment(runtime, "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED", options.ProcessingWorkTempStaleSweepMovieScheduleEnabled)));
        services.AddSingleton<IPeriodicEnqueuer>(sp => new WorkTempStaleSweepEnqueuer(
            sp.GetRequiredService<ProcessingJobStore>(), "tv", TimeSpan.FromSeconds(options.ProcessingWorkTempStaleSweepTvScheduleIntervalSeconds),
            SwitchedOffByEnvironment(runtime, "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED", options.ProcessingWorkTempStaleSweepTvScheduleEnabled)));
        services.AddSingleton<IPeriodicEnqueuer>(sp => new FailureCleanupSweepEnqueuer(
            sp.GetRequiredService<ProcessingJobStore>(), "movie", TimeSpan.FromSeconds(options.ProcessingMovieFailureCleanupScheduleIntervalSeconds),
            SwitchedOffByEnvironment(runtime, "WEIR_PROCESSING_MOVIE_FAILURE_CLEANUP_SCHEDULE_ENABLED", options.ProcessingMovieFailureCleanupScheduleEnabled)));
        services.AddSingleton<IPeriodicEnqueuer>(sp => new FailureCleanupSweepEnqueuer(
            sp.GetRequiredService<ProcessingJobStore>(), "tv", TimeSpan.FromSeconds(options.ProcessingTvFailureCleanupScheduleIntervalSeconds),
            SwitchedOffByEnvironment(runtime, "WEIR_PROCESSING_TV_FAILURE_CLEANUP_SCHEDULE_ENABLED", options.ProcessingTvFailureCleanupScheduleEnabled)));

        // #652: hand-back copies nobody claimed. Off until a person switches it on in Settings › Cleanup.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, UnclaimedHandbackCleanupHandler>());
        services.AddSingleton<IPeriodicEnqueuer>(sp => new UnclaimedHandbackCleanupEnqueuer(sp.GetRequiredService<ProcessingJobStore>(), "movie"));
        services.AddSingleton<IPeriodicEnqueuer>(sp => new UnclaimedHandbackCleanupEnqueuer(sp.GetRequiredService<ProcessingJobStore>(), "tv"));

        // Recovery runs in the background (#718) and completes before either worker lane claims its first job.
        services.AddSingleton<JobsStartupRecoveryService>();
        services.AddHostedService(sp => sp.GetRequiredService<JobsStartupRecoveryService>());
        services.TryAddSingleton<JobRowsRetention>();
        services.AddSingleton<IPeriodicTask, JobRowsRetentionTask>();
        services.AddWeirPeriodicTasks();
        // When each Cleanup family next runs, for Settings › Cleanup.
        services.AddSingleton<PeriodicEnqueueClock>();
        services.AddHostedService<PeriodicEnqueueService>();
        if (options.ProcessingWorkerCount > 0)
        {
            services.AddHostedService<ProcessingWorkerService>();
            // The upkeep lane (#717): scans and maintenance sweeps, on slots of their own beside the file lane.
            services.AddHostedService<UpkeepWorkerService>();
        }

        return services;
    }

    /// <summary>Whether the environment variable was set and reads as off, which stops its timer whatever is saved.</summary>
    private static bool SwitchedOffByEnvironment(RuntimeEnvironment runtime, string variable, bool enabled) =>
        runtime.IsSet(variable) && !enabled;
}
