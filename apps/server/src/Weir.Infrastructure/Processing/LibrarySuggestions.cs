using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// The libraries first-run setup can offer once a media manager or download client is connected: one per media type, with
/// the watched folder the connection already knows and a default output folder beside it. Composes what the library editor
/// already reads (<see cref="ManagerSetupCheck.SuggestFoldersAsync"/> for Sonarr, Radarr and Deluno,
/// <see cref="DownloadClientSuggestions"/> for bare download clients) and only ever reads: nothing is created, and nothing
/// on a manager or download client is changed.
/// </summary>
/// <remarks>
/// A new install starts with an empty Movies and an empty TV library, so an offer fills one of those in when its media
/// type still has one with no folders, and is a new library only when the first library of that type is already in use.
/// </remarks>
public sealed class LibrarySuggestions
{
    private readonly ManagerSetupCheck _managerSetup;
    private readonly DownloadClientSuggestions _downloadClients;
    private readonly LibraryStore _libraries;

    public LibrarySuggestions(ManagerSetupCheck managerSetup, DownloadClientSuggestions downloadClients, LibraryStore libraries)
    {
        _managerSetup = managerSetup ?? throw new ArgumentNullException(nameof(managerSetup));
        _downloadClients = downloadClients ?? throw new ArgumentNullException(nameof(downloadClients));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
    }

    public async Task<LibrarySuggestionSet> SuggestAsync(UnitOfWork uow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var clients = await _downloadClients.ReadAllAsync(uow, cancellationToken).ConfigureAwait(false);
        var takenNames = (await _libraries.ListAsync(uow).ConfigureAwait(false)).Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var libraries = new List<SuggestedLibrary>();
        var notes = new List<string>();

        foreach (var scope in ProcessingMediaScopes.All)
        {
            var managers = await _managerSetup.SuggestFoldersAsync(uow, scope, cancellationToken).ConfigureAwait(false);
            if (Choose(scope, managers, clients) is not { } source)
            {
                notes.AddRange(managers.Select(manager => manager.Problem).OfType<string>());
                continue;
            }

            if (libraries.FirstOrDefault(library => SameFolder(library.WatchedFolder, source.Watched)) is { } sharing)
            {
                notes.Add($"{source.Label} saves {sharing.Name} and {LibraryFolderSuggestionRules.LibraryName(scope)} downloads to the same folder, so Weir suggests one workflow for them. Add another in Settings › Workflows once they are kept apart.");
                continue;
            }

            var empty = await EmptyLibraryAsync(uow, scope).ConfigureAwait(false);
            var name = empty?.Name ?? LibraryDiscoveryService.UniqueName(takenNames, LibraryFolderSuggestionRules.LibraryName(scope));
            takenNames.Add(name);
            libraries.Add(new SuggestedLibrary(
                empty?.Id,
                name,
                scope,
                source.Watched,
                source.Output ?? LibraryFolderSuggestionRules.DefaultOutputFolder(source.Watched, scope) ?? string.Empty,
                source.Label,
                [.. managers.Select(manager => manager.ConnectionId)]));
        }

        return new LibrarySuggestionSet(libraries, [.. notes.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The first library of a media type, when nothing has been set up in it yet.</summary>
    private async Task<ProcessingLibraryRecord?> EmptyLibraryAsync(UnitOfWork uow, string scope) =>
        await _libraries.SeededForScopeAsync(uow, scope).ConfigureAwait(false) is { WatchedFolder.Length: 0, OutputFolder.Length: 0 } first ? first : null;

    /// <summary>A media manager's own folder first, since it is the one that hands files over; a bare download client's after it.</summary>
    private static FolderSource? Choose(
        string scope,
        IReadOnlyList<ManagerFolderSuggestion> managers,
        IReadOnlyList<(DownloadClientConnectionRecord Row, DownloadClientFolders Folders)> clients)
    {
        if (managers.FirstOrDefault(manager => !string.IsNullOrEmpty(manager.WatchedFolder)) is { } manager)
        {
            return new FolderSource(manager.Label, manager.WatchedFolder!, manager.OutputFolder);
        }

        foreach (var (row, folders) in clients)
        {
            if (LibraryFolderSuggestionRules.DownloadClientFolderFor(scope, folders) is { } folder)
            {
                return new FolderSource(row.Label, folder, null);
            }
        }

        return null;
    }

    private static bool SameFolder(string first, string second) =>
        LibraryRules.NormalizeFolder(first) == LibraryRules.NormalizeFolder(second);

    private sealed record FolderSource(string Label, string Watched, string? Output);
}
