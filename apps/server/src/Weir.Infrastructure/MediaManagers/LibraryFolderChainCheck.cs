using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// One per-library "folder chain" check: Weir's own watched/work/output folders
/// (<see cref="LibraryFolderChainRules"/>) plus, for every enabled connection that covers the library's media type,
/// whether that manager will actually pick up what Weir writes (<see cref="ManagerSetupCheck"/>, unchanged — this only
/// calls its public method). With no manager connected the Weir-only chain is already a complete, valid setup: an empty
/// manager list can never make the overall result not ready, because <c>List.All</c> over an empty list is true.
/// </summary>
public sealed class LibraryFolderChainCheck
{
    private readonly ManagerSetupCheck _managerSetupCheck;
    private readonly DownloadClientSuggestions _downloadClientSuggestions;
    private readonly LibraryStore _libraries;
    private readonly WeirOptions _options;
    private readonly IFolderProbe _probe;

    public LibraryFolderChainCheck(ManagerSetupCheck managerSetupCheck, DownloadClientSuggestions downloadClientSuggestions, LibraryStore libraries, WeirOptions options)
        : this(managerSetupCheck, downloadClientSuggestions, libraries, options, new FilesystemFolderProbe())
    {
    }

    /// <summary>For tests: the filesystem comes from <paramref name="probe"/> instead of the real disk.</summary>
    internal LibraryFolderChainCheck(
        ManagerSetupCheck managerSetupCheck, DownloadClientSuggestions downloadClientSuggestions, LibraryStore libraries, WeirOptions options, IFolderProbe probe)
    {
        _managerSetupCheck = managerSetupCheck ?? throw new ArgumentNullException(nameof(managerSetupCheck));
        _downloadClientSuggestions = downloadClientSuggestions ?? throw new ArgumentNullException(nameof(downloadClientSuggestions));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>
    /// One library's chain: <c>{ library_id, local: { ready, lines }, managers: [...], download_clients: [...], ready }</c>.
    /// <c>managers</c> is exactly what <see cref="ManagerSetupCheck.CheckAsync"/> already returns for this library's
    /// watched/output folders and media type, reused as-is. <c>download_clients</c> is one entry per enabled bare
    /// download-client connection, checking whether any of its folders is this library's watched folder
    /// (<see cref="LibraryFolderChainRules.CheckDownloadClientFolder"/>) — with none connected, an empty list never
    /// makes the library not ready, the same as with no manager connected.
    /// </summary>
    public async Task<WireObject> CheckForLibraryAsync(UnitOfWork uow, ProcessingLibraryRecord library, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(library);

        var folderRow = new ProcessingLibraryFolderRow(library.Id, library.MediaType, (int)library.DisplayOrder, library.WorkFolder, library.OutputFolder);
        var workFolder = ProcessingLibraryFolders.EffectiveWorkFolder(folderRow, _options.WeirHome);
        var workFolderIsDefault = string.IsNullOrWhiteSpace(library.WorkFolder);
        var localLines = LibraryFolderChainRules.CheckLocalFolders(library.WatchedFolder, workFolder, workFolderIsDefault, library.OutputFolder, _probe);
        var localReady = localLines.All(line => line.State != SetupCheckLine.Problem);

        var managers = await _managerSetupCheck
            .CheckAsync(uow, library.MediaType, library.WatchedFolder, library.OutputFolder, library.RemoveOriginalAfterSuccess, cancellationToken)
            .ConfigureAwait(false);
        var managersReady = managers.All(entry => entry.Get("ready") is WireBool { Value: true });

        var downloadClients = await DownloadClientEntriesAsync(uow, library.WatchedFolder, cancellationToken).ConfigureAwait(false);
        var downloadClientsReady = downloadClients.All(entry => entry.Get("ready") is WireBool { Value: true });

        return new WireObject()
            .Set("library_id", library.Id)
            .Set("local", new WireObject()
                .Set("ready", localReady)
                .Set("lines", new WireArray(localLines.Select(line => (WireValue)line.ToOut()))))
            .Set("managers", new WireArray(managers.Select(entry => (WireValue)entry)))
            .Set("download_clients", new WireArray(downloadClients.Select(entry => (WireValue)entry)))
            .Set("ready", localReady && managersReady && downloadClientsReady);
    }

    private async Task<List<WireObject>> DownloadClientEntriesAsync(UnitOfWork uow, string watchedFolder, CancellationToken cancellationToken)
    {
        var connections = await _downloadClientSuggestions.ReadAllAsync(uow, cancellationToken).ConfigureAwait(false);
        return [.. connections.Select(item =>
        {
            var label = DownloadClientKinds.LabelForConnection(item.Row.Kind, item.Row.Name);
            var line = LibraryFolderChainRules.CheckDownloadClientFolder(label, watchedFolder, item.Folders);
            return new WireObject()
                .Set("connection_id", item.Row.Id)
                .Set("kind", item.Row.Kind)
                .Set("name", item.Row.Name)
                .Set("label", label)
                .Set("ready", line.State != SetupCheckLine.Problem)
                .Set("lines", new WireArray([(WireValue)line.ToOut()]));
        })];
    }

    /// <summary>
    /// The same chain check for every library linked to one connection (Settings › Media managers wants "for this
    /// connection, which of its linked libraries are fully chained"), in library order.
    /// </summary>
    public async Task<List<WireObject>> CheckForConnectionAsync(UnitOfWork uow, long connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var libraries = await _libraries.LibrariesForConnectionIdAsync(uow, connectionId).ConfigureAwait(false);
        var results = new List<WireObject>();
        foreach (var library in libraries)
        {
            results.Add(await CheckForLibraryAsync(uow, library, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
