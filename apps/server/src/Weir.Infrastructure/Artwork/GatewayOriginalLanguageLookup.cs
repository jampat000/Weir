using Weir.Core.Artwork;
using Weir.Core.MediaManagers;
using Weir.Core.Rules;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>
/// A title's original language, from Deluno's metadata service. It is read from the same lookup that finds the title's poster, under
/// the same key (the TMDb id a media manager gave, otherwise the title and year; a series by its name, not its episode), so a title
/// is asked about once whichever of the two needs it first. When the background pass has not reached the title yet, it is asked about
/// now, through the same limiter. Declining is an answer: the rules fall back to the language preferences.
/// </summary>
public sealed class GatewayOriginalLanguageLookup : IOriginalLanguageLookup
{
    private readonly SqliteDatabase _database;
    private readonly ArtworkLookupStore _lookups;
    private readonly ArtworkFileStore _files;
    private readonly ArtworkSubjects _subjects;
    private readonly ArtworkResolver _resolver;
    private readonly ArtworkGatewayClient _gateway;
    private readonly ArtworkRateLimiter _limiter;
    private readonly TimeProvider _time;

    public GatewayOriginalLanguageLookup(
        SqliteDatabase database,
        ArtworkLookupStore lookups,
        ArtworkFileStore files,
        ArtworkSubjects subjects,
        ArtworkResolver resolver,
        ArtworkGatewayClient gateway,
        ArtworkRateLimiter limiter,
        TimeProvider time)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _lookups = lookups ?? throw new ArgumentNullException(nameof(lookups));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<LookupResult> LookupAsync(string mediaScope, long? libraryId, string relativeMediaPath, HandoffOrigin? origin, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
        {
            return Declined(LookupResult.StatusNotConfigured, "the metadata lookup is switched off on this server");
        }

        var lookup = await FindOrQueueAsync(mediaScope, libraryId, relativeMediaPath, origin, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            return Declined(LookupResult.StatusNoMatch, "Weir could not read a title from the file name");
        }

        if (Answer(lookup) is { } known)
        {
            return known;
        }

        if (lookup.OriginalLanguage is null && lookup.IsDue && !_limiter.IsPaused)
        {
            await _resolver.SearchNowAsync(lookup, cancellationToken).ConfigureAwait(false);
            lookup = await ReadAsync(lookup.Key, cancellationToken).ConfigureAwait(false) ?? lookup;
        }

        return Answer(lookup) ?? Unanswered(lookup);
    }

    /// <summary>The lookup of the title the file belongs to: the one it was linked to when it arrived, or the one its name gives.</summary>
    private async Task<ArtworkLookup?> FindOrQueueAsync(string mediaScope, long? libraryId, string relativeMediaPath, HandoffOrigin? origin, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var now = _time.GetUtcNow();
            var linkedKey = libraryId is { } library ? await _files.LookupKeyAsync(uow, library, relativeMediaPath).ConfigureAwait(false) : null;
            if (linkedKey is not null && await _lookups.FindAsync(uow, linkedKey, now).ConfigureAwait(false) is { } linked && CanBeSearched(linked))
            {
                return linked;
            }

            if (ArtworkTitleReader.Read(ArtworkSubjects.ScopeOf(mediaScope), relativeMediaPath, origin?.ReleaseName) is not { } title)
            {
                return null;
            }

            var key = await _subjects.QueueTitleAsync(uow, mediaScope, title, ArtworkPriority.Processing).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
            return await _lookups.FindAsync(uow, key, now).ConfigureAwait(false);
        }
    }

    private async Task<ArtworkLookup?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            return await _lookups.FindAsync(uow, key, _time.GetUtcNow()).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the service can be asked about the title: it has a name or an id. A lookup that only knows a poster address cannot.</summary>
    private static bool CanBeSearched(ArtworkLookup lookup) => lookup.Title.Length > 0 || lookup.TmdbId is not null || lookup.TvdbId is not null;

    /// <summary>The language the service reported, when it reported one.</summary>
    private static LookupResult? Answer(ArtworkLookup lookup) =>
        string.IsNullOrEmpty(lookup.OriginalLanguage)
            ? null
            : new LookupResult
            {
                Status = LookupResult.StatusMatched,
                Metadata = new TitleMetadata { OriginalLanguage = lookup.OriginalLanguage, Title = lookup.Title, Year = lookup.Year },
                Detail = "Deluno's metadata service reported the original language",
            };

    /// <summary>Why there is no language: the service said none, it does not know the title, or it could not be asked just now.</summary>
    private static LookupResult Unanswered(ArtworkLookup lookup) =>
        lookup.OriginalLanguage is not null
            ? Declined(LookupResult.StatusNoMatch, "the metadata service did not say what language this title was made in")
            : lookup.Outcome == ArtworkOutcomes.Pending
                ? Declined(LookupResult.StatusUnreachable, "Weir could not reach the metadata service just now")
                : Declined(LookupResult.StatusNoMatch, "the metadata service does not know this title");

    private static LookupResult Declined(string status, string detail) => new() { Status = status, Detail = detail };
}
