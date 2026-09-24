using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// "What folder could this bare download client be pointed at" for every enabled connection — read only,
/// the same shape as <see cref="ManagerSetupCheck"/> for media managers: Weir never writes a download client's
/// settings, only suggests. A connection whose client did not answer contributes nothing, rather than an entry
/// with no folder or failing the whole list.
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
        return [.. (await ReadAllAsync(uow, cancellationToken).ConfigureAwait(false)).Select(item => Entry(item.Row, item.Folders))];
    }

    /// <summary>
    /// Every enabled connection whose client answered, with its own folders — the same read <see cref="SuggestAsync"/>
    /// does, reused by the folder chain check so it needs no second round trip to each client.
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

            var folders = await port.ReadFoldersAsync(connection, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(folders.CompletedFolder) && folders.CategoryFolders.Count == 0)
            {
                continue;
            }

            results.Add((row, folders));
        }

        return results;
    }

    private static WireObject Entry(DownloadClientConnectionRecord row, DownloadClientFolders folders)
    {
        var label = DownloadClientKinds.LabelForConnection(row.Kind, row.Name);
        var lines = new List<SetupCheckLine>();
        if (!string.IsNullOrEmpty(folders.CompletedFolder))
        {
            lines.Add(new SetupCheckLine(SetupCheckLine.Ok, $"{label}'s default completed-downloads folder is {folders.CompletedFolder}."));
        }

        foreach (var category in folders.CategoryFolders.Where(category => category.Folder != folders.CompletedFolder))
        {
            lines.Add(new SetupCheckLine(SetupCheckLine.Note, $"{label} saves the \"{category.Category}\" category to {category.Folder}."));
        }

        return new WireObject()
            .Set("connection_id", row.Id)
            .Set("kind", row.Kind)
            .Set("name", row.Name)
            .Set("label", label)
            .Set("flow", "download_client")
            .Set("ready", !string.IsNullOrEmpty(folders.CompletedFolder))
            .Set("lines", new WireArray(lines.Select(line => (WireValue)line.ToOut())))
            .Set("suggested_watched_folder", folders.CompletedFolder)
            .Set("category_folders", new WireArray(folders.CategoryFolders.Select(category =>
                (WireValue)new WireObject().Set("category", category.Category).Set("folder", category.Folder))));
    }
}
