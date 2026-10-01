using Microsoft.Extensions.Logging;
using Weir.Core.Artwork;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>
/// Finds the poster for each queued title, one at a time and within <see cref="ArtworkRateLimiter"/>'s limits: asks the metadata
/// service, stores the image, and remembers what came of it. A title is asked about once; one the service does not know is
/// asked about again after a week, and one that could not be asked is retried with growing waits.
/// </summary>
public sealed class ArtworkResolver
{
    private readonly SqliteDatabase _database;
    private readonly ArtworkLookupStore _lookups;
    private readonly ArtworkGatewayClient _gateway;
    private readonly ArtworkPosterFiles _posterFiles;
    private readonly ArtworkRateLimiter _limiter;
    private readonly TimeProvider _time;
    private readonly ILogger<ArtworkResolver> _logger;

    public ArtworkResolver(
        SqliteDatabase database,
        ArtworkLookupStore lookups,
        ArtworkGatewayClient gateway,
        ArtworkPosterFiles posterFiles,
        ArtworkRateLimiter limiter,
        TimeProvider time,
        ILogger<ArtworkResolver> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _lookups = lookups ?? throw new ArgumentNullException(nameof(lookups));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _posterFiles = posterFiles ?? throw new ArgumentNullException(nameof(posterFiles));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Settle up to <paramref name="maxLookups"/> due titles, stopping early when the service is busy or cannot be reached. Returns how many were settled.</summary>
    public async Task<int> ResolveDueAsync(int maxLookups, CancellationToken cancellationToken)
    {
        var settled = 0;
        while (settled < maxLookups && !_limiter.IsPaused && await NextDueAsync(cancellationToken).ConfigureAwait(false) is { } lookup)
        {
            if (!await ResolveAsync(lookup, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            settled++;
        }

        return settled;
    }

    private async Task<ArtworkLookup?> NextDueAsync(CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            return await _lookups.NextDueAsync(uow, _time.GetUtcNow()).ConfigureAwait(false);
        }
    }

    /// <summary>Settle one title. False when the service cannot take more work for now, so the caller stops.</summary>
    private async Task<bool> ResolveAsync(ArtworkLookup lookup, CancellationToken cancellationToken)
    {
        var posterRef = lookup.PosterRef;
        if (posterRef is null)
        {
            await _limiter.WaitForSearchAsync(cancellationToken).ConfigureAwait(false);
            var match = await _gateway.FindAsync(lookup, cancellationToken).ConfigureAwait(false);
            if (match.Status != GatewayStatus.Ok)
            {
                return await SettleWithoutPosterAsync(lookup, match.Status, match.RetryAfter, cancellationToken).ConfigureAwait(false);
            }

            posterRef = match.Value!.PosterRef;
        }

        var posterId = ArtworkKeys.PosterId(posterRef);
        if (!await HasPosterAsync(posterId, cancellationToken).ConfigureAwait(false))
        {
            var image = await _gateway.FetchPosterAsync(posterRef, cancellationToken).ConfigureAwait(false);
            if (image.Status != GatewayStatus.Ok)
            {
                return await SettleWithoutPosterAsync(lookup, image.Status, image.RetryAfter, cancellationToken).ConfigureAwait(false);
            }

            await StorePosterAsync(posterId, posterRef, image.Value!, cancellationToken).ConfigureAwait(false);
        }

        await WriteAsync(uow => _lookups.RecordFoundAsync(uow, lookup.Key, posterId), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HasPosterAsync(string posterId, CancellationToken cancellationToken)
    {
        if (!_posterFiles.Exists(posterId))
        {
            return false;
        }

        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            return await _lookups.FindPosterAsync(uow, posterId).ConfigureAwait(false) is not null;
        }
    }

    private async Task StorePosterAsync(string posterId, string posterRef, GatewayBody image, CancellationToken cancellationToken)
    {
        await _posterFiles.WriteAsync(posterId, image.Bytes, cancellationToken).ConfigureAwait(false);
        await WriteAsync(uow => _lookups.AddPosterAsync(uow, posterId, posterRef, image.ContentType, image.Bytes.Length), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SettleWithoutPosterAsync(ArtworkLookup lookup, GatewayStatus status, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        switch (status)
        {
            case GatewayStatus.NotFound:
                await WriteAsync(uow => _lookups.RecordMissingAsync(uow, lookup.Key, now + ArtworkSchedule.MissingRetryAfter), cancellationToken).ConfigureAwait(false);
                return true;
            case GatewayStatus.Busy:
                _limiter.HonourBusy(retryAfter);
                _logger.LogInformation("The metadata service asked Weir to wait before looking up more posters.");
                return false;
            default:
                var attempts = lookup.Attempts + 1;
                await WriteAsync(uow => _lookups.RecordFailureAsync(uow, lookup.Key, attempts, now + ArtworkSchedule.FailureBackoff(attempts)), cancellationToken).ConfigureAwait(false);
                _limiter.HonourUnreachable();
                return false;
        }
    }

    private async Task WriteAsync(Func<UnitOfWork, Task> write, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            await write(uow).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }
}
