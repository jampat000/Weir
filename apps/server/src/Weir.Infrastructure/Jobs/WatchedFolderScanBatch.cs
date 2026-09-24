using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// A watched-folder scan's writes, gathered while it looks at files and applied a few hundred at a time, each batch in its
/// own short transaction (#708). The scan holds the write lock only while a batch is written, never while it walks folders,
/// reads files or queues passes, so hand-offs, passes, progress and sign-ins are not held up behind it.
/// </summary>
internal sealed class WatchedFolderScanBatch
{
    /// <summary>State writes per transaction: small enough that another writer waits milliseconds, not seconds.</summary>
    internal const int WritesPerBatch = 250;

    /// <summary>Rows marked seen per transaction: one statement for all of them, so many more fit in the same time.</summary>
    internal const int TouchesPerBatch = 2_000;

    private readonly SqliteDatabase _database;
    private readonly long _libraryId;
    private readonly DateTimeOffset _seenAt;
    private readonly List<long> _touched = [];
    private readonly List<(ScannedFileWrite Write, Func<Task>? WhenApplied)> _writes = [];
    private readonly List<Func<Task>> _afterCommit = [];

    public WatchedFolderScanBatch(SqliteDatabase database, long libraryId, DateTimeOffset seenAt)
    {
        _database = database;
        _libraryId = libraryId;
        _seenAt = seenAt;
    }

    public bool IsFull => _writes.Count + _afterCommit.Count >= WritesPerBatch || _touched.Count >= TouchesPerBatch;

    /// <summary>Marks a row seen by this scan.</summary>
    public void Touch(long rowId) => _touched.Add(rowId);

    /// <summary>Records a file's state; <paramref name="whenApplied"/> runs after the batch commits, and only if the write applied.</summary>
    public void Record(ScannedFileWrite write, Func<Task>? whenApplied = null) => _writes.Add((write, whenApplied));

    /// <summary>Runs <paramref name="work"/> once this batch has committed.</summary>
    public void AfterCommit(Func<Task> work) => _afterCommit.Add(work);

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_touched.Count + _writes.Count + _afterCommit.Count == 0)
        {
            return;
        }

        var followUps = new List<Func<Task>>(_afterCommit);
        await WriteLockTurns.TakeAsync(
            async () =>
            {
                var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
                await using (uow.ConfigureAwait(false))
                {
                    await FileStateStore.TouchLastSeenAsync(uow, _touched, _seenAt).ConfigureAwait(false);
                    foreach (var (write, whenApplied) in _writes)
                    {
                        if (await FileStateStore.RecordScannedStateAsync(uow, _libraryId, write, _seenAt).ConfigureAwait(false) && whenApplied is not null)
                        {
                            followUps.Add(whenApplied);
                        }
                    }

                    await uow.CommitAsync().ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        _touched.Clear();
        _writes.Clear();
        _afterCommit.Clear();
        foreach (var followUp in followUps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await followUp().ConfigureAwait(false);
        }
    }
}
