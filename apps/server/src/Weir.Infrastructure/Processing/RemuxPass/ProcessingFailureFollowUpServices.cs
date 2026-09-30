using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// Registers #522 part 4: what happens once a failure policy has decided (pass-through, reject), the opt-in reject
/// policy's support gate, completing the seams <c>AddWeirRemuxPass</c> left open.
/// </summary>
public static class ProcessingFailureFollowUpServices
{
    public static IServiceCollection AddWeirProcessingFailureFollowUps(this IServiceCollection services, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddWeirRemuxPass(options);

        services.TryAddSingleton<RejectSupportEvaluator>();
        services.TryAddSingleton<RejectRoutes>();
        services.TryAddSingleton<HistoryFileRemovalService>();
        services.TryAddSingleton<RejectPacing>();

        services.TryAddSingleton<ProcessingPassThroughHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, ProcessingPassThroughHandler>(sp => sp.GetRequiredService<ProcessingPassThroughHandler>()));

        services.TryAddSingleton<ProcessingRejectHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, ProcessingRejectHandler>(sp => sp.GetRequiredService<ProcessingRejectHandler>()));

        return services;
    }
}
