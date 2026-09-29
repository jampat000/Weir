using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Places a hand-off whose file a manager names by another route to the watched folder (a NAS mount, a junction) when it
/// does not translate the path itself. <see cref="HandoffPaths"/> compares text only, so this runs after it finds nothing.
/// </summary>
public static class HandoffThroughLinks
{
    /// <summary>
    /// The library whose watched folder holds the file once junctions and links are followed. Nothing is chosen unless the
    /// file and the watched folder both resolve on this machine and the file really is inside it; the library returned keeps
    /// its own watched folder, so the relative path still joins onto it.
    /// </summary>
    public static (IntakeLibrary Library, HandoffPathResult Resolved)? ChooseLibrary(IReadOnlyList<IntakeLibrary> libraries, MediaManagerImportEvent importEvent)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(importEvent);
        if (FinalPaths.Resolve(importEvent.FilePath) is not { } finalFile)
        {
            return null;
        }

        var resolvedLibraries = libraries
            .Select(library => FinalPaths.Resolve(library.WatchedFolder) is { } finalFolder ? library with { WatchedFolder = finalFolder } : null)
            .OfType<IntakeLibrary>()
            .ToList();
        return IntakeRules.ChooseLibrary(resolvedLibraries, importEvent with { FilePath = finalFile }) is { } chosen
            ? (libraries.First(library => library.Id == chosen.Library.Id), chosen.Resolved)
            : null;
    }
}
