using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// One library's published folders: the stable, read-only shape a media manager reads instead of asking a
/// person to retype the same paths. Weir owns the watched, work and output folders; the manager owns its own
/// library roots and categories. <see cref="ManagerLibraryKey"/> is the manager's own id for the library the workflow was set
/// up from, and <see cref="FoldersFromManager"/> says whether that manager still owns the watched and output folders.
/// <see cref="MinimumFileSizeBytes"/> is the workflow's minimum file size, null when it has none, so a manager can leave smaller files
/// out of a hand-off instead of sending them for Weir to refuse.
/// </summary>
public sealed record PublishedLibraryFolders(
    long Id,
    string Name,
    string MediaType,
    string WatchedFolder,
    string WorkFolder,
    string OutputFolder,
    string? ManagerLibraryKey,
    bool FoldersFromManager,
    long? MinimumFileSizeBytes)
{
    public WireObject ToOut() => new WireObject()
        .Set("id", Id)
        .Set("name", Name)
        .Set("media_type", MediaType)
        .Set("watched_folder", WatchedFolder)
        .Set("work_folder", WorkFolder)
        .Set("output_folder", OutputFolder)
        .Set("manager_library_key", ManagerLibraryKey)
        .Set("folders_from_manager", FoldersFromManager)
        .Set("minimum_file_size_bytes", MinimumFileSizeBytes);

    /// <summary>A workflow's minimum in bytes, from its setting in MB: null for no minimum.</summary>
    public static long? MinimumBytesOf(long minFileSizeMb) => minFileSizeMb > 0 ? minFileSizeMb * 1024 * 1024 : null;

    /// <summary>
    /// The manager library a workflow was set up from, while it is still set up from that manager: null once it is unlinked
    /// or if it never came from one.
    /// </summary>
    public static string? ManagerLibraryKeyOf(long? discoveredFromConnectionId, string? discoveredLibraryKey) =>
        discoveredFromConnectionId is not null && !string.IsNullOrEmpty(discoveredLibraryKey) ? discoveredLibraryKey : null;
}

/// <summary>The intake capability and shape for reading every enabled library's folders in one call.</summary>
public static class LibraryFolderPublishing
{
    /// <summary>The intake capability that says Weir takes <c>GET /intake/library-folders</c>.</summary>
    public const string Capability = "library-folders";

    public static WireObject ToOut(IReadOnlyList<PublishedLibraryFolders> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        return new WireObject().Set("libraries", new WireArray(libraries.Select(library => (WireValue)library.ToOut())));
    }
}
