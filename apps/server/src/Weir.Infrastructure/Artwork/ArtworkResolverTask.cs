using Weir.Core.Artwork;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>
/// <c>artwork-resolver</c>: every few seconds, titles any new files and looks up the posters that are due. It looks nothing up while
/// Artwork is switched off or no gateway is configured, and a pass is cut short when the service is busy. About once an hour it also
/// prunes what files that are gone leave behind, which needs no network.
/// </summary>
public sealed class ArtworkResolverTask : IPeriodicTask
{
    /// <summary>The most titles one pass looks up, so a pass ends in good time and sees a change to the setting.</summary>
    private const int MaxLookupsPerPass = 20;

    private readonly SqliteDatabase _database;
    private readonly ArtworkGatewayClient _gateway;
    private readonly ArtworkDiscovery _discovery;
    private readonly ArtworkResolver _resolver;
    private readonly ArtworkPruner _pruner;
    private readonly TimeProvider _time;
    private DateTimeOffset _nextPruneAt = DateTimeOffset.MinValue;

    public ArtworkResolverTask(
        SqliteDatabase database, ArtworkGatewayClient gateway, ArtworkDiscovery discovery, ArtworkResolver resolver, ArtworkPruner pruner, TimeProvider time)
    {
        _pruner = pruner ?? throw new ArgumentNullException(nameof(pruner));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public string Name => "artwork-resolver";

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => TimeSpan.FromSeconds(30);

    public string FailureMessage => "Looking up posters failed; Weir will try again shortly";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await PruneWhenDueAsync(cancellationToken).ConfigureAwait(false);
        if (!_gateway.IsConfigured || !await IsOnAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        await _resolver.ResolveDueAsync(MaxLookupsPerPass, cancellationToken).ConfigureAwait(false);
    }

    private async Task PruneWhenDueAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (now < _nextPruneAt)
        {
            return;
        }

        _nextPruneAt = now + ArtworkSchedule.PruneInterval;
        await _pruner.PruneAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsOnAsync(CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            return await ArtworkSwitch.IsOnAsync(uow).ConfigureAwait(false);
        }
    }
}
