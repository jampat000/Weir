using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.ConnectionTraffic;

/// <summary>
/// Saves <see cref="ConnectionUsageLedger"/>'s newest values every ten seconds, in one short write. However many calls a
/// connection sees in that time, it costs at most one write, so a busy queue poll does not hammer SQLite.
/// </summary>
public sealed class ConnectionUsageFlushTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly ConnectionUsageLedger _ledger;
    private readonly MediaManagerConnectionStore _managers;
    private readonly DownloadClientConnectionStore _clients;

    public ConnectionUsageFlushTask(
        SqliteDatabase database, ConnectionUsageLedger ledger, MediaManagerConnectionStore managers, DownloadClientConnectionStore clients)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _managers = managers ?? throw new ArgumentNullException(nameof(managers));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    public string Name => "connection-usage-flush";

    public string? Label => null;

    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Saving when each connection was last used failed.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var unsaved = _ledger.TakeUnsaved();
        if (unsaved.Count == 0)
        {
            return;
        }

        try
        {
            var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                foreach (var (connection, usage) in unsaved)
                {
                    await SaveAsync(uow, connection, usage).ConfigureAwait(false);
                }

                await uow.CommitAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Puts the values back for the next run and rethrows, so the failure is still logged.
        catch (Exception)
#pragma warning restore CA1031
        {
            _ledger.ReturnUnsaved(unsaved.Select(entry => entry.Connection));
            throw;
        }
    }

    private Task<int> SaveAsync(UnitOfWork uow, ConnectionRef connection, ConnectionUsage usage) =>
        connection.Kind == ConnectionKind.MediaManager
            ? _managers.RecordUsageAsync(uow, connection.Id, usage)
            : _clients.RecordUsageAsync(uow, connection.Id, usage);
}
