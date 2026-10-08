using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Api.Endpoints;
using Weir.Core.Configuration;
using Weir.Infrastructure.Http;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.SystemLog;

namespace Weir.Api;

/// <summary>Registers what System's overview, scheduled tasks and log feed are built from.</summary>
public static class SystemOverviewServices
{
    public static IServiceCollection AddWeirSystemOverview(this IServiceCollection services, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // The launcher registers how it started and where it listens before this; a host that did not reads as a plain app.
        services.TryAddSingleton(ServerRunMode.App);
        services.TryAddSingleton(new ServerListenOptions(ServerListenOptions.DefaultHost, ServerListenOptions.DefaultPort));
        services.TryAddSingleton<ServerStartStore>();
        services.TryAddSingleton(sp => new DataFootprint(options.WeirHome, sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<UpdateStatusReader>();
        services.TryAddSingleton<UpdateOutlook>();
        services.TryAddSingleton<SystemChecks>();
        services.TryAddSingleton<SystemOverviewReader>();
        services.TryAddSingleton<SystemOverviewEndpointHandlers>();
        services.TryAddSingleton<SystemTasksFrames>();
        services.TryAddSingleton<SystemChecksFrames>();
        services.TryAddSingleton<SystemOverviewFrames>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, ReadinessChangeTask>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, SystemOverviewChangeTask>());
        services.TryAddSingleton<SystemLogFrames>();
        services.TryAddSingleton<SystemLogReader>();
        services.TryAddSingleton<SystemLogEndpointHandlers>();
        return services;
    }
}
