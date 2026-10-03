using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>
/// Finds files that have no title yet, reads each one's title from its name and queues it, newest first. Files a media manager
/// handed over are titled at intake; this covers the rest: a scan, a watched folder, a library's own files.
/// </summary>
public sealed class ArtworkDiscovery
{
    private const int BatchSize = 200;

    private readonly SqliteDatabase _database;
    private readonly ArtworkFileStore _files;
    private readonly ArtworkSubjects _subjects;

    public ArtworkDiscovery(SqliteDatabase database, ArtworkFileStore files, ArtworkSubjects subjects)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
    }

    public async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        await TitleAllAsync(_files.UnlinkedProcessingFilesAsync, ArtworkPriority.Processing, cancellationToken).ConfigureAwait(false);
        await TitleAllAsync(_files.UnlinkedLibraryFilesAsync, ArtworkPriority.Library, cancellationToken).ConfigureAwait(false);
    }

    private async Task TitleAllAsync(Func<UnitOfWork, int, Task<List<ArtworkCandidate>>> untitled, int priority, CancellationToken cancellationToken)
    {
        while (true)
        {
            var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var batch = await untitled(uow, BatchSize).ConfigureAwait(false);
                foreach (var file in batch)
                {
                    await _subjects.LinkFromNameAsync(uow, file, priority).ConfigureAwait(false);
                }

                await uow.CommitAsync().ConfigureAwait(false);
                if (batch.Count < BatchSize)
                {
                    return;
                }
            }
        }
    }
}
