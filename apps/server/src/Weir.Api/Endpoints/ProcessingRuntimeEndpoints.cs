using Microsoft.AspNetCore.Routing;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Media;

namespace Weir.Api.Endpoints;

/// <summary>Read-only runtime probes: what is running right now, the worker/runtime snapshot, and hardware-acceleration detection.</summary>
public static class ProcessingRuntimeEndpoints
{
    public static IEndpointRouteBuilder MapProcessingRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapV1("GET", "/processing/runtime-settings", GetRuntimeSettingsAsync);
        endpoints.MapV1("GET", "/processing/files-at-once", GetFilesAtOnceAsync);
        endpoints.MapV1("GET", "/processing/hardware", GetHardwareAsync);
        return endpoints;
    }

    /// <summary>What is running, what is waiting and which limit the waiting files are waiting on (#633).</summary>
    private static async Task<ApiResult> GetFilesAtOnceAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var store = request.Service<ProcessingJobStore>();
        var now = request.Service<TimeProvider>().GetUtcNow();
        var slots = request.Options.ProcessingWorkerCount;
        // A read, so never the queue's write transaction (#636): under several remuxes that queued behind the workers
        // until it timed out with "database is locked".
        var readout = await store.ReadAsync(
            (connection, transaction) => WorkAdmissionReader.ReadFilesAtOnce(connection, transaction, now, slots)).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("files_at_once", readout.FilesAtOnce)
            .Set("worker_slots", readout.WorkerSlots)
            .Set("effective_files_at_once", readout.Effective)
            .Set("running", readout.Running)
            .Set("waiting", readout.Waiting)
            .Set("waiting_for", readout.WaitingFor)
            .Set("message", readout.Message)
            .Set("slots_note", readout.SlotsNote));
    }

    private static async Task<ApiResult> GetRuntimeSettingsAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var settings = RuntimeVisibility.From(request.Options);
        return ApiRoutes.Ok(new WireObject()
            .Set("in_process_processing_worker_count", settings.InProcessProcessingWorkerCount)
            .Set("in_process_workers_disabled", settings.InProcessWorkersDisabled)
            .Set("in_process_workers_enabled", settings.InProcessWorkersEnabled)
            .Set("worker_mode_summary", settings.WorkerModeSummary)
            .Set("sqlite_throughput_note", settings.SqliteThroughputNote)
            .Set("configuration_note", settings.ConfigurationNote)
            .Set("visibility_note", settings.VisibilityNote)
            .Set("processing_media_extensions", new WireArray(settings.ProcessingMediaExtensions.Select(e => (WireValue)WireValue.Of(e))))
            .Set("processing_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs", settings.ProcessingWatchedFolderRemuxScanDispatchPeriodicEnqueueRemuxJobs)
            .Set("processing_probe_size_mb", settings.ProcessingProbeSizeMb)
            .Set("processing_analyze_duration_seconds", settings.ProcessingAnalyzeDurationSeconds)
            .Set("processing_watched_folder_min_file_age_seconds", settings.ProcessingWatchedFolderMinFileAgeSeconds)
            .Set("processing_movie_output_cleanup_min_age_seconds", settings.ProcessingMovieOutputCleanupMinAgeSeconds)
            .Set("movie_output_cleanup_configuration_note", settings.MovieOutputCleanupConfigurationNote)
            .Set("processing_tv_output_cleanup_min_age_seconds", settings.ProcessingTvOutputCleanupMinAgeSeconds)
            .Set("tv_output_cleanup_configuration_note", settings.TvOutputCleanupConfigurationNote)
            .Set("watched_folder_scan_periodic_configuration_note", settings.WatchedFolderScanPeriodicConfigurationNote)
            .Set("processing_work_temp_stale_sweep_movie_schedule_enabled", settings.ProcessingWorkTempStaleSweepMovieScheduleEnabled)
            .Set("processing_work_temp_stale_sweep_movie_schedule_interval_seconds", settings.ProcessingWorkTempStaleSweepMovieScheduleIntervalSeconds)
            .Set("processing_work_temp_stale_sweep_tv_schedule_enabled", settings.ProcessingWorkTempStaleSweepTvScheduleEnabled)
            .Set("processing_work_temp_stale_sweep_tv_schedule_interval_seconds", settings.ProcessingWorkTempStaleSweepTvScheduleIntervalSeconds)
            .Set("processing_work_temp_stale_sweep_min_stale_age_seconds", settings.ProcessingWorkTempStaleSweepMinStaleAgeSeconds)
            .Set("processing_movie_failure_cleanup_schedule_enabled", settings.ProcessingMovieFailureCleanupScheduleEnabled)
            .Set("processing_movie_failure_cleanup_schedule_interval_seconds", settings.ProcessingMovieFailureCleanupScheduleIntervalSeconds)
            .Set("processing_tv_failure_cleanup_schedule_enabled", settings.ProcessingTvFailureCleanupScheduleEnabled)
            .Set("processing_tv_failure_cleanup_schedule_interval_seconds", settings.ProcessingTvFailureCleanupScheduleIntervalSeconds)
            .Set("processing_movie_failure_cleanup_grace_period_seconds", settings.ProcessingMovieFailureCleanupGracePeriodSeconds)
            .Set("processing_tv_failure_cleanup_grace_period_seconds", settings.ProcessingTvFailureCleanupGracePeriodSeconds)
            .Set("failure_cleanup_configuration_note", settings.FailureCleanupConfigurationNote)
            .Set("work_temp_stale_sweep_periodic_configuration_note", settings.WorkTempStaleSweepPeriodicConfigurationNote));
    }

    private static async Task<ApiResult> GetHardwareAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        IMediaToolResolver resolver = request.Service<IMediaToolResolver>();
        string ffmpeg;
        try
        {
            (_, ffmpeg) = resolver.Resolve();
        }
        catch (MediaToolException exception)
        {
            return ApiRoutes.Ok(new WireObject()
                .Set("detected", false)
                .Set("available_methods", new WireArray([]))
                .Set("vendors", new WireArray([]))
                .Set("selectable_vendors", new WireArray(HardwareAcceleration.VendorMethods.Select(v => v.Key).Order(StringComparer.Ordinal).Select(k => (WireValue)WireValue.Of(k))))
                .Set("strictness_levels", new WireArray(HardwareAcceleration.StrictnessLevels.Select(l => (WireValue)WireValue.Of(l))))
                .Set("detail", $"Weir could not find ffmpeg, so it cannot report acceleration methods. {exception.Message}"));
        }

        var mediaTools = request.Service<MediaTools>();
        var report = await mediaTools.DetectAccelerationAsync(ffmpeg, request.Context.RequestAborted).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject()
            .Set("detected", report.Detected)
            .Set("available_methods", new WireArray(report.AvailableMethods.Select(m => (WireValue)WireValue.Of(m))))
            .Set("vendors", new WireArray(report.Vendors.Select(v => (WireValue)WireValue.Of(v))))
            .Set("selectable_vendors", new WireArray(HardwareAcceleration.VendorMethods.Select(v => v.Key).Order(StringComparer.Ordinal).Select(k => (WireValue)WireValue.Of(k))))
            .Set("strictness_levels", new WireArray(HardwareAcceleration.StrictnessLevels.Select(l => (WireValue)WireValue.Of(l))))
            .Set("detail", report.Detail));
    }
}
