using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Api.Endpoints;
using Weir.Core.Jobs;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Scheduling;

namespace Weir.Api;

/// <summary>
/// DI registration and route wiring for the Processing libraries, file state, files/inspection/maintenance/
/// overview/hardware/Direct Play APIs (#522). One extension per side, additive only: it never touches
/// <see cref="Weir.Api.WeirApi"/>'s own registrations, so other areas can add their own <c>AddWeirXxx</c>
/// alongside this without conflict.
/// </summary>
public static class ProcessingApi
{
    /// <summary>Services the Processing endpoints need beyond those <c>AddWeirApi</c> and <c>AddWeirJobs</c>
    /// already register.</summary>
    public static IServiceCollection AddWeirProcessingApis(this IServiceCollection services)
    {
        services.AddWeirMediaTools();
        // Caps the #502 "Try on a file" preview at one run at a time (see RulesPreviewGate's own docs).
        services.TryAddSingleton<RulesPreviewGate>();

        // The only "processing-file-log-retention" task (#546): it uses the same UnitOfWork/
        // OperatorSettingsStore/FileLogStore plumbing as the rest of Processing.
        services.AddSingleton<FileLogRetentionTask>();
        services.AddSingleton<IPeriodicTask>(sp => sp.GetRequiredService<FileLogRetentionTask>());

        // Watched-folder scan dispatch (#522 part 5): the job handler that runs a scan, and the periodic
        // scheduler that enqueues one per library. Additive: the job handler registry (AddWeirJobs) simply
        // gains one more kind it can claim, and the periodic task joins the others already registered.
        services.AddSingleton<ScanWakeups>();
        services.AddSingleton<ProcessingWatchedFolderScanDispatchJobHandler>();
        services.AddSingleton<IJobHandler>(sp => sp.GetRequiredService<ProcessingWatchedFolderScanDispatchJobHandler>());
        services.AddSingleton<IPeriodicTask, ProcessingWatchedFolderScanDispatchScheduleTask>();
        // Forgets the rows of files that left a watched folder even when that library's scan is off.
        services.AddSingleton<IPeriodicTask, VanishedFileSweepTask>();

        // Watched-folder filesystem watcher (#552): FileSystemWatcher per enabled/watched library, feeding
        // the same scan-dispatch enqueue above. Runs independently of the periodic scheduler — see
        // ProcessingWatchedFolderWatcherService's own docs for what it does.
        services.AddHostedService<ProcessingWatchedFolderWatcherService>();
        services.AddHostedService<WorkFolderPlacementCheck>();
        return services;
    }

    /// <summary>Maps every Processing route (see the docs on each endpoint class for what it covers).</summary>
    public static IEndpointRouteBuilder MapWeirProcessingApis(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapProcessingLibraryEndpoints();
        endpoints.MapProcessingLibraryDiscoveryEndpoints();
        endpoints.MapProcessingRuleSetsEndpoints();
        endpoints.MapProcessingFilesEndpoints();
        endpoints.MapProcessingLibraryCleansEndpoints();
        endpoints.MapProcessingDirectPlayEndpoints();
        endpoints.MapProcessingJobsEndpoints();
        endpoints.MapProcessingRemuxPassEndpoints();
        endpoints.MapProcessingRulesPreviewEndpoints();
        endpoints.MapProcessingOverviewMaintenanceEndpoints();
        endpoints.MapProcessingSettingsEndpoints();
        endpoints.MapProcessingWatchedFolderScanDispatchEndpoints();
        endpoints.MapLibraryModeEndpoints();
        endpoints.MapLibraryModeOverviewEndpoints();
        endpoints.MapLibraryModeFilesEndpoints();
        endpoints.MapLibraryModeScheduleEndpoints();
        endpoints.MapLibraryModeRedownloadsEndpoints();
        return endpoints;
    }
}
