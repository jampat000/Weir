using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// The one registration of <see cref="ProcessingJobStore"/>. The job queue and the areas that enqueue into it all
/// call this, so the store is built the same way whichever registers first.
/// </summary>
public static class JobStoreServices
{
    public static IServiceCollection AddWeirJobStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<WorkerWakeSignals>();
        services.TryAddSingleton(sp => new ProcessingJobStore(
            sp.GetRequiredService<SqliteDatabase>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IJobQueueMetrics>(),
            sp.GetRequiredService<WorkerWakeSignals>()));
        return services;
    }
}
