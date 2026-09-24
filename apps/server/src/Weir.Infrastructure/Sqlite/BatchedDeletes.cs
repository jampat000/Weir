namespace Weir.Infrastructure.Sqlite;

/// <summary>
/// Deletes a large set of rows a batch per transaction, taking turns on the write lock between batches (#708). Pruning history
/// in one transaction held the lock for as long as the whole delete took; on the first run after an upgrade that was seconds,
/// and every other lane waited for it.
/// </summary>
public static class BatchedDeletes
{
    /// <summary>Rows deleted per transaction.</summary>
    public const int BatchSize = 5_000;

    /// <summary>
    /// Deletes every row of <paramref name="table"/> that <paramref name="condition"/> selects; returns how many went. Both are
    /// the caller's own SQL, never text from a request; values go in <paramref name="parameters"/>.
    /// </summary>
    public static async Task<int> DeleteAsync(
        SqliteDatabase database, string table, string condition, (string Name, object? Value)[] parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        var sql = $"DELETE FROM {table} WHERE id IN (SELECT id FROM {table} WHERE {condition} LIMIT {BatchSize})";
        var total = 0;
        int deleted;
        do
        {
            deleted = 0;
            await WriteLockTurns.TakeAsync(
                async () =>
                {
                    var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
                    await using (uow.ConfigureAwait(false))
                    {
                        deleted = await uow.ExecuteAsync(sql, parameters).ConfigureAwait(false);
                        await uow.CommitAsync().ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }
}
