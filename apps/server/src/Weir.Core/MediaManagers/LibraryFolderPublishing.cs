using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// One library's published folders (#768): the stable, read-only shape a media manager reads instead of asking a
/// person to retype the same paths. Weir owns the watched, work and output folders; the manager owns its own
/// library roots and categories.
/// </summary>
public sealed record PublishedLibraryFolders(long Id, string Name, string MediaType, string WatchedFolder, string WorkFolder, string OutputFolder)
{
    public WireObject ToOut() => new WireObject()
        .Set("id", Id)
        .Set("name", Name)
        .Set("media_type", MediaType)
        .Set("watched_folder", WatchedFolder)
        .Set("work_folder", WorkFolder)
        .Set("output_folder", OutputFolder);
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
