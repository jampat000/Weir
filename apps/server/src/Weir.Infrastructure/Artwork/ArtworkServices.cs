using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Artwork;

/// <summary>Registers posters: the lookup queue, the metadata service client, and the background resolver.</summary>
public static class ArtworkServices
{
    public static IServiceCollection AddWeirArtwork(this IServiceCollection services)
    {
        services.TryAddSingleton<ArtworkLookupStore>();
        services.TryAddSingleton<ArtworkFileStore>();
        services.TryAddSingleton<ArtworkSubjects>();
        services.TryAddSingleton<ArtworkPosterUrls>();
        services.TryAddSingleton<ArtworkPosterFiles>();
        services.TryAddSingleton<ArtworkRateLimiter>();
        services.TryAddSingleton<ArtworkGatewayClient>();
        services.TryAddSingleton<ArtworkDiscovery>();
        services.TryAddSingleton<ArtworkResolver>();
        services.TryAddSingleton<ArtworkPruner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, ArtworkResolverTask>());
        return services;
    }
}
