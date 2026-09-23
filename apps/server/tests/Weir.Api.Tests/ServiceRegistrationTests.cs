using Microsoft.Extensions.DependencyInjection;
using Weir.Core.Jobs;
using Weir.Core.Library;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Library;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Scheduling;

namespace Weir.Api.Tests;

/// <summary>
/// The real server's service registrations. When two areas register the same service, the first one wins, so a
/// second, different registration silently does nothing or silently wins depending on order.
/// </summary>
public sealed class ServiceRegistrationTests
{
    /// <summary>Services several parts register on purpose; every one of them is used.</summary>
    private static readonly HashSet<Type> RegisteredManyTimesByDesign = [typeof(IPeriodicTask), typeof(IPeriodicEnqueuer), typeof(IJobHandler)];

    [Fact]
    public async Task Removed_track_history_is_kept_in_the_database()
    {
        await using var server = await WeirTestServer.StartAsync();

        Assert.IsType<FileLogRemovedTrackStore>(server.Services.GetRequiredService<IRemovedTrackStore>());
    }

    [Fact]
    public async Task Redownload_services_resolve_to_their_one_implementation()
    {
        await using var server = await WeirTestServer.StartAsync();

        Assert.IsType<InMemoryRedownloadTracker>(server.Services.GetRequiredService<IRedownloadTracker>());
        Assert.IsType<ArrManagerRedownload>(server.Services.GetRequiredService<IManagerRedownload>());
    }

    [Fact]
    public async Task No_weir_service_is_added_twice()
    {
        IServiceCollection? registered = null;
        await using var server = await WeirTestServer.StartAsync(configureServices: services => registered = services);

        var duplicates = registered!
            .Where(descriptor => descriptor.ServiceType.Namespace?.StartsWith("Weir.", StringComparison.Ordinal) == true)
            .GroupBy(descriptor => descriptor.ServiceType)
            .Where(group => group.Count() > 1 && !RegisteredManyTimesByDesign.Contains(group.Key))
            .Select(group => group.Key.FullName)
            .ToList();

        Assert.Empty(duplicates);
    }
}
