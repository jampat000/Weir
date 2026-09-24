using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.Configuration;
using Weir.Core.MediaManagers;
using Weir.Core.Security;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>Registers the media manager area (#520): connections, ports, intake, the hand-off ledger and completion reports.</summary>
public static class MediaManagerServices
{
    public static IServiceCollection AddWeirMediaManagers(this IServiceCollection services, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddWeirPlatform(options);
        services.TryAddSingleton(sp => new CredentialCipher(
            options.CredentialsSecret, options.SessionSecret, options.PreviousCredentialsSecrets, sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IManagerHttpHandlerFactory, SocketsManagerHttpHandlerFactory>();
        services.TryAddSingleton<IMediaManagerPorts, HttpMediaManagerPorts>();
        services.TryAddSingleton<MediaManagerConnectionService>();
        // Every minute, each manager's connection test, so Weir knows within a minute when one goes quiet.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, ManagerHeartbeatTask>());
        services.TryAddSingleton<LibraryDiscoveryService>();
        services.TryAddSingleton<ManagerSetupCheck>();
        services.TryAddSingleton<HandoffLedgerStore>();
        services.AddWeirJobStore();
        services.TryAddSingleton<MediaManagerIntake>();
        services.TryAddSingleton<HandoffCompletionReporter>();
        // #652: what a manager said about a file Weir handed back, and the one rule that releases Weir's copy.
        services.TryAddSingleton<HandbackOutcomes>();
        // The optional downloaded-scan hand-back for a Sonarr/Radarr connection outside Weir's own hand-off flow.
        services.TryAddSingleton<DownloadedScanNotifier>();
        services.TryAddSingleton<MetadataProviderService>();
        services.TryAddSingleton<ILibraryFileChangeNotifier, LibraryFileChangeNotifier>();

        // #509: asking a manager to redownload a title. What a clean removed, and which titles wait for a
        // redownload, belong to library mode and are registered there, once.
        services.TryAddSingleton<IManagerRedownload, ArrManagerRedownload>();
        return services;
    }
}
