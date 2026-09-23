using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Core.Configuration;
using Weir.Core.Library;
using Weir.Core.MediaManagers;
using Weir.Core.Security;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Library;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

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
        // Every minute, each manager's connection test, so Weir knows within a minute when one goes quiet (23 Sep 2026).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPeriodicTask, ManagerHeartbeatTask>());
        services.TryAddSingleton<LibraryDiscoveryService>();
        services.TryAddSingleton<ManagerSetupCheck>();
        services.TryAddSingleton<HandoffLedgerStore>();
        services.TryAddSingleton(sp => new ProcessingJobStore(
            sp.GetRequiredService<SqliteDatabase>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IJobQueueMetrics>()));
        services.TryAddSingleton<MediaManagerIntake>();
        services.TryAddSingleton<HandoffCompletionReporter>();
        // #652: what a manager said about a file Weir handed back, and the one rule that releases Weir's copy.
        services.TryAddSingleton<HandbackOutcomes>();
        services.TryAddSingleton<MetadataProviderService>();
        services.TryAddSingleton<ILibraryFileChangeNotifier, LibraryFileChangeNotifier>();

        // #509: redownloading a title after a removed track can no longer be restored. The store and
        // tracker default to in-memory (see their remarks for why); FileLogRemovedTrackStore is available
        // to opt into instead once a caller wants the payload-backed history to survive a restart.
        services.TryAddSingleton<IManagerRedownload, ArrManagerRedownload>();
        services.TryAddSingleton<IRemovedTrackStore, InMemoryRemovedTrackStore>();
        services.TryAddSingleton<IRedownloadTracker, InMemoryRedownloadTracker>();
        return services;
    }
}
