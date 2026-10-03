using Weir.Core.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

public sealed partial class ManagerSetupCheck
{
    /// <summary>
    /// The download clients the enabled Sonarr or Radarr connections covering <paramref name="mediaScope"/> say they use, so
    /// a workflow can be checked against the bare clients it really depends on. <paramref name="linkedConnectionIds"/>
    /// narrows it to the managers a workflow is linked to; null means every one that covers the media type, as for folders not
    /// yet saved. Deluno names its clients but not their product or address, so it contributes none. A manager that does
    /// not answer contributes none: its own check already says so.
    /// </summary>
    public async Task<List<ArrDownloadClientEntry>> DownloadClientsUsedAsync(
        UnitOfWork uow, string mediaScope, IReadOnlySet<long>? linkedConnectionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var clients = new List<ArrDownloadClientEntry>();
        foreach (var row in await _connectionStore.ListEnabledAsync(uow).ConfigureAwait(false))
        {
            if (!IsArrFor(row, mediaScope) ||
                (linkedConnectionIds is not null && !linkedConnectionIds.Contains(row.Id)) ||
                _connections.ConnectionFromRow(row) is not { } connection)
            {
                continue;
            }

            try
            {
                var client = new MediaManagerHttpClient(connection.BaseUrl, connection.ApiKey, _handlers, ManagerDialectRules.DescribeTimeout, connection.Reference);
                var payload = await client.GetJsonAsync(ManagerSetupRules.DownloadClientPath, cancellationToken: cancellationToken).ConfigureAwait(false);
                clients.AddRange(ManagerSetupRules.ParseDownloadClients(payload, mediaScope));
            }
            catch (Exception exception) when (exception is MediaManagerHttpException or MediaManagerUnreachableException)
            {
                LogManagerDidNotAnswer(_logger, exception, connection.Label, "which download clients it uses");
            }
        }

        return clients;
    }
}
