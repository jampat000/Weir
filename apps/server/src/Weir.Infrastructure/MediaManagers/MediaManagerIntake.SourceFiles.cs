using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

public sealed partial class MediaManagerIntake
{
    /// <summary>
    /// The files a hand-off lists in its <c>sourceFiles</c>, as paths in the library. Only these are the hand-off's: the rest of the
    /// folder is not read, recorded or reported. A file that lies outside the folder the hand-off names, or that is not there, refuses
    /// the whole hand-off rather than falling back to the folder.
    /// </summary>
    public static List<string> HandoffListedFiles(IntakeLibrary library, string relativePath, MediaManagerImportEvent importEvent)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(importEvent);
        var resolved = HandoffSourceFiles.Resolve(importEvent.FilePath, relativePath, importEvent.SourceFiles ?? [], OperatingSystem.IsWindows());
        if (!resolved.Ok)
        {
            throw new IntakeRefusedException(400, resolved.Problem!);
        }

        foreach (var target in resolved.RelativePaths!)
        {
            if (!File.Exists(Path.Join(library.WatchedFolder, target)))
            {
                throw new IntakeRefusedException(400, HandoffSourceFiles.MissingDetail(target));
            }
        }

        return [.. resolved.RelativePaths];
    }
}
