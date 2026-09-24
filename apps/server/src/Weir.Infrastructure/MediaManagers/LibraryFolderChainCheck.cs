using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// One per-library "folder chain" check for #768: Weir's own watched/work/output folders
/// (<see cref="LibraryFolderChainRules"/>) plus, for every enabled connection that covers the library's media type,
/// whether that manager will actually pick up what Weir writes (<see cref="ManagerSetupCheck"/>, unchanged — this only
/// calls its public method). With no manager connected the Weir-only chain is already a complete, valid setup: an empty
/// manager list can never make the overall result not ready, because <c>List.All</c> over an empty list is true.
/// </summary>
public sealed class LibraryFolderChainCheck
{
    private readonly ManagerSetupCheck _managerSetupCheck;
    private readonly WeirOptions _options;
    private readonly IFolderProbe _probe;

    public LibraryFolderChainCheck(ManagerSetupCheck managerSetupCheck, WeirOptions options)
        : this(managerSetupCheck, options, new FilesystemFolderProbe())
    {
    }

    /// <summary>For tests: the filesystem comes from <paramref name="probe"/> instead of the real disk.</summary>
    internal LibraryFolderChainCheck(ManagerSetupCheck managerSetupCheck, WeirOptions options, IFolderProbe probe)
    {
        _managerSetupCheck = managerSetupCheck ?? throw new ArgumentNullException(nameof(managerSetupCheck));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>
    /// One library's chain: <c>{ library_id, local: { ready, lines }, managers: [...], ready }</c>. <c>managers</c> is
    /// exactly what <see cref="ManagerSetupCheck.CheckAsync"/> already returns for this library's watched/output
    /// folders and media type, reused as-is.
    /// </summary>
    public async Task<PyDict> CheckForLibraryAsync(UnitOfWork uow, ProcessingLibraryRecord library, CancellationToken cancellationToken)
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
        var managersReady = managers.All(entry => entry.Get("ready") is PyBool { Value: true });

        return new PyDict()
            .Set("library_id", library.Id)
            .Set("local", new PyDict()
                .Set("ready", localReady)
                .Set("lines", new PyList(localLines.Select(line => (PyJson)line.ToOut()))))
            .Set("managers", new PyList(managers.Select(entry => (PyJson)entry)))
            .Set("ready", localReady && managersReady);
    }

    /// <summary>
    /// The same chain check for every library linked to one connection (Settings › Media managers wants "for this
    /// connection, which of its linked libraries are fully chained"), in library order.
    /// </summary>
    public async Task<List<PyDict>> CheckForConnectionAsync(UnitOfWork uow, long connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var libraries = await LibraryStore.LibrariesForConnectionIdAsync(uow, connectionId).ConfigureAwait(false);
        var results = new List<PyDict>();
        foreach (var library in libraries)
        {
            results.Add(await CheckForLibraryAsync(uow, library, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
