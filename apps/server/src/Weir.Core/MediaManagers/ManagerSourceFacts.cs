namespace Weir.Core.MediaManagers;

/// <summary>
/// What a connected media manager calls the places a workflow's files come from and go to, in its own words: the
/// category it files this media type's downloads under in its download client, the library (Deluno) or root folder
/// (Sonarr, Radarr) it imports the cleaned file into. Any of them is null when the manager did not report it, so a
/// sentence built from these is only ever shorter, never guessed.
/// </summary>
public sealed record ManagerSourceFacts(string? Category, string? ManagerLibrary, string? RootFolder)
{
    public static readonly ManagerSourceFacts None = new(null, null, null);
}

/// <summary>Reads <see cref="ManagerSourceFacts"/> from what a manager already reports; pure, no I/O.</summary>
public static class ManagerSourceFactsRules
{
    /// <summary>Deluno: the first Refine-before-import library for the media type, and its first enabled download client's category.</summary>
    public static ManagerSourceFacts ForDeluno(
        string mediaScope,
        IReadOnlyList<ManagerLibraryDescriptor> libraries,
        IReadOnlyList<ManagerDownloadClientDescriptor>? downloadClients)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var library = libraries.FirstOrDefault(candidate => candidate.MediaScope == mediaScope && candidate.ProcessesBeforeImport);
        var category = (downloadClients ?? [])
            .Where(client => client.Enabled)
            .Select(client => mediaScope == MediaManagerKinds.Tv ? client.TvCategory : client.MoviesCategory)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        return new ManagerSourceFacts(category, NonEmpty(library?.Name), null);
    }

    /// <summary>Sonarr and Radarr: the first enabled download client's category for the media type, and the first root folder.</summary>
    public static ManagerSourceFacts ForArr(IReadOnlyList<ArrDownloadClientEntry> downloadClients, IReadOnlyList<string> rootFolders)
    {
        ArgumentNullException.ThrowIfNull(downloadClients);
        ArgumentNullException.ThrowIfNull(rootFolders);
        var category = downloadClients.Where(client => client.Enabled).Select(client => NonEmpty(client.Category)).FirstOrDefault(name => name is not null);
        return new ManagerSourceFacts(category, null, rootFolders.Select(NonEmpty).FirstOrDefault(path => path is not null));
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
