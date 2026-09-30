using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// "What folder could this bare download client be pointed at" for every enabled connection — read only: Weir never
/// writes a download client's settings, only suggests. A suggestion says where the client saves and nothing about
/// whether that suits a workflow: it is not compared with any watched folder here, so it carries no ready or fine
/// verdict. <see cref="LibraryFolderChainRules.CheckDownloadClientFolders"/> owns that comparison. A connection whose
/// client did not answer contributes nothing, rather than an entry with no folder or failing the whole list.
/// </summary>
public sealed class DownloadClientSuggestions
{
    private readonly DownloadClientConnectionService _connections;
    private readonly DownloadClientConnectionStore _store;
    private readonly IDownloadClientPorts _ports;

    public DownloadClientSuggestions(DownloadClientConnectionService connections, DownloadClientConnectionStore store, IDownloadClientPorts ports)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    public async Task<List<WireObject>> SuggestAsync(UnitOfWork uow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return [.. (await ReadAllAsync(uow, cancellationToken).ConfigureAwait(false))
            .Where(item => !string.IsNullOrEmpty(item.Folders.CompletedFolder) || item.Folders.CategoryFolders.Count > 0)
            .Select(item => Entry(item.Row, item.Folders))];
    }

    /// <summary>
    /// Every enabled connection with its own folders — the same read <see cref="SuggestAsync"/> does, reused by the folder
    /// chain check so it needs no second round trip to each client. A client that did not answer is still listed, with
    /// <see cref="DownloadClientFolders.Empty"/>, so the check can say it could not verify it.
    /// </summary>
    public async Task<List<(DownloadClientConnectionRecord Row, DownloadClientFolders Folders)>> ReadAllAsync(UnitOfWork uow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var results = new List<(DownloadClientConnectionRecord, DownloadClientFolders)>();
        foreach (var row in await _store.ListEnabledAsync(uow).ConfigureAwait(false))
        {
            if (_connections.ConnectionFromRow(row) is not { } connection || _ports.PortForKind(row.Kind) is not { } port)
            {
                continue;
            }

            results.Add((row, await port.ReadFoldersAsync(connection, cancellationToken).ConfigureAwait(false)));
        }

        return results;
    }

    private static WireObject Entry(DownloadClientConnectionRecord row, DownloadClientFolders folders)
    {
        return new WireObject()
            .Set("connection_id", row.Id)
            .Set("kind", row.Kind)
            .Set("name", row.Name)
            .Set("label", row.Label)
            .Set("suggested_watched_folder", folders.CompletedFolder)
            .Set("category_folders", new WireArray(folders.CategoryFolders.Select(category =>
                (WireValue)new WireObject().Set("category", category.Category).Set("folder", category.Folder))));
    }
}
