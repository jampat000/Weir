using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.Configuration;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>Registers the readers, the sampler and the store behind the System view.</summary>
public static class SystemStatsServices
{
    public static IServiceCollection AddWeirSystemStats(this IServiceCollection services, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton<ToolProcessLedger>();
        services.TryAddSingleton(HostReadingSources.ForThisMachine());
        services.TryAddSingleton<MachineMeter>();
        services.TryAddSingleton<MachineFactsReader>();
        services.TryAddSingleton<FreeSpaceForecast>();
        services.TryAddSingleton<DriveReader>();
        services.TryAddSingleton<ProcessingThroughput>();
        services.TryAddSingleton<IWorkSource, DatabaseWorkSource>();
        services.TryAddSingleton(sp => new SystemStatsStore(sp.GetRequiredService<TimeProvider>(), Environment.ProcessorCount));
        services.TryAddSingleton(sp => new SystemStatsCollector(
            sp.GetRequiredService<MachineMeter>(),
            sp.GetRequiredService<ProcessingThroughput>(),
            () => sp.GetRequiredService<ToolProcessLedger>().TotalProcessorTime,
            OwnProcessUse.OfThisProcess,
            sp.GetRequiredService<TimeProvider>(),
            Environment.ProcessorCount));
        services.TryAddSingleton(sp => ActivatorUtilities.CreateInstance<SystemStatsSampler>(sp, options.ProcessingWorkerCount));
        services.AddHostedService(sp => sp.GetRequiredService<SystemStatsSampler>());
        return services;
    }
}
