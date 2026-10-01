using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>The posters Weir's own address serves, and which files show them.</summary>
public sealed class ArtworkPosterUrls
{
    private const string Prefix = "/api/v1/artwork/posters/";

    private readonly ArtworkFileStore _files;

    public ArtworkPosterUrls(ArtworkFileStore files)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>The address a poster is served at.</summary>
    public static string UrlFor(string posterId) => Prefix + posterId;

    /// <summary>
    /// The poster address of each given file that has one. It is empty while Artwork is switched off, so a switched-off
    /// install shows no posters even though images it fetched earlier stay on disk.
    /// </summary>
    public async Task<IReadOnlyDictionary<(long LibraryId, string Path), string>> ForFilesAsync(UnitOfWork uow, IEnumerable<(long LibraryId, string Path)> files)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(files);
        var wanted = files.ToList();
        if (wanted.Count == 0 || !await ArtworkSwitch.IsOnAsync(uow).ConfigureAwait(false))
        {
            return new Dictionary<(long, string), string>();
        }

        var ids = await _files.PosterIdsAsync(uow, wanted).ConfigureAwait(false);
        return ids.ToDictionary(pair => pair.Key, pair => UrlFor(pair.Value));
    }
}
