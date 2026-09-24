using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

public static partial class ManagerSetupRules
{
    /// <summary>
    /// Deluno hands each finished download to Weir over its API and imports the result itself, so nothing needs mapping.
    /// What Weir can read reliably from its manifest (<c>GET /api/integrations/external/manifest</c>) is each library's
    /// workflow, its <c>downloadsPath</c> ("downloads arrive in") and its <c>processorOutputPath</c>. A hand-off's file
    /// has to sit inside the watched folder (<see cref="HandoffPaths.RelativeMediaPathForHandoff"/>), compared the same
    /// case- and separator-insensitive way here.
    /// </summary>
    public static DelunoSetupResult EvaluateDeluno(
        string managerLabel, string mediaScope, string watchedFolder, string outputFolder, IReadOnlyList<ManagerLibraryDescriptor> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var scopeWord = mediaScope == MediaManagerKinds.Tv ? "TV" : "movie";
        var lines = new List<SetupCheckLine>();
        var refining = libraries.Where(library => library.MediaScope == mediaScope && library.ProcessesBeforeImport).ToList();
        if (refining.Count == 0)
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Problem,
                $"No {managerLabel} {scopeWord} library is set to Refine before import, so {managerLabel} will not hand {scopeWord} downloads to Weir. " +
                $"Choose Refine before import for that library in {managerLabel}."));
            return new DelunoSetupResult(null, null, lines);
        }

        var library = refining[0];
        if (refining.Count > 1)
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Note,
                $"{managerLabel} has {refining.Count} {scopeWord} libraries set to Refine before import; the folders below are {library.Name}'s. " +
                "Give each its own Weir library."));
        }

        var downloads = WireStrings.Strip(library.DownloadsPath ?? string.Empty);
        var watched = WireStrings.Strip(watchedFolder ?? string.Empty);
        if (downloads.Length == 0)
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Note,
                $"{managerLabel} does not say where {library.Name}'s downloads arrive. Its hand-offs have to sit inside this library's watched folder."));
        }
        else if (watched.Length > 0 && Inside(downloads, watched))
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Ok,
                $"{library.Name}'s downloads arrive in {downloads}, inside the watched folder, so Weir accepts its hand-offs."));
        }
        else
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Problem,
                $"{library.Name}'s downloads arrive in {downloads}, which is not inside the watched folder, so Weir would refuse its hand-offs. " +
                $"Use {managerLabel}'s folders."));
        }

        var processed = WireStrings.Strip(library.OutputPath ?? string.Empty);
        var output = WireStrings.Strip(outputFolder ?? string.Empty);
        if (processed.Length > 0)
        {
            lines.Add(output.Length > 0 && Inside(processed, output) && Inside(output, processed)
                ? new SetupCheckLine(SetupCheckLine.Ok, $"{managerLabel} picks up cleaned files from {processed}, the folder this library writes to.")
                : new SetupCheckLine(
                    SetupCheckLine.Problem,
                    $"{managerLabel} picks up cleaned files from {processed}, but this library writes to {(output.Length > 0 ? output : "no output folder")}. " +
                    "Unless both are the same folder seen from two machines, use the same folder."));
        }

        return new DelunoSetupResult(downloads.Length > 0 ? downloads : null, processed.Length > 0 ? processed : null, lines);
    }

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it: case- and separator-insensitive, as <c>HandoffPaths</c> compares.</summary>
    private static bool Inside(string path, string folder)
    {
        var target = Comparable(path);
        var root = Comparable(folder);
        return root.Length > 0 && (target == root || target.StartsWith(root + "/", StringComparison.Ordinal));
    }

    private static string Comparable(string path) => WireStrings.Strip(path.Replace('\\', '/')).TrimEnd('/').ToLowerInvariant();
}
