using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.ConnectionTraffic;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Registers the bare download-client area: connections, their five dialects, and the suggestion read.
/// Depends on <see cref="Weir.Core.Security.CredentialCipher"/> and <see cref="IManagerHttpHandlerFactory"/>,
/// which <c>AddWeirMediaManagers</c> already registers — call that first.
/// </summary>
public static class DownloadClientServices
{
    public static IServiceCollection AddWeirDownloadClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DownloadClientConnectionStore>();
        services.TryAddSingleton<DownloadClientConnectionService>();
        services.TryAddSingleton<IDownloadClientPorts, DownloadClientPorts>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDownloadClientPort, SabnzbdPort>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDownloadClientPort, NzbgetPort>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDownloadClientPort, QBittorrentPort>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDownloadClientPort, DelugePort>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDownloadClientPort, TransmissionPort>());
        services.TryAddSingleton<DownloadClientSuggestions>();
        // Every ten seconds, when each connection (manager or client) was last used and how long it took to answer.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, ConnectionUsageFlushTask>());
        return services;
    }
}
