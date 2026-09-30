using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>What processing every rejected file again would do right now.</summary>
/// <param name="Rejected">How many files are rejected.</param>
/// <param name="Ready">How many of those can be processed again: their original is still in the watched folder.</param>
public sealed record RejectedFilesSummary(int Rejected, int Ready);

/// <summary>
/// Putting the whole set of rejected files back to work after the rules changed. Each file goes through
/// <see cref="RequeueStore"/>, exactly as "Process again" on that one file does, so a file whose original is gone or
/// whose workflow was removed is skipped and counted rather than queued.
/// </summary>
public sealed class RejectedFilesAgain(FileStateStore files, RequeueStore requeue)
{
    public async Task<RejectedFilesSummary> SummarizeAsync(UnitOfWork uow, long? libraryId)
    {
        var rejected = await files.ListRejectedAsync(uow, libraryId).ConfigureAwait(false);
        var ready = 0;
        foreach (var row in rejected)
        {
            if (await requeue.CanRequeueAsync(uow, row).ConfigureAwait(false))
            {
                ready++;
            }
        }

        return new RejectedFilesSummary(rejected.Count, ready);
    }

    public async Task<RequeueResult> ProcessAgainAsync(UnitOfWork uow, long? libraryId)
    {
        var rejected = await files.ListRejectedAsync(uow, libraryId).ConfigureAwait(false);
        return await requeue.RequeueFilesAsync(uow, rejected).ConfigureAwait(false);
    }
}
