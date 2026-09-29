using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

public static partial class ManagerSetupRules
{
    /// <summary>
    /// Deluno hands each finished download to Weir over its API and imports the result itself, so nothing needs mapping.
    /// What Weir can read reliably from its manifest (<c>GET /api/integrations/external/manifest</c>) is each library's
    /// workflow, its <c>downloadsPath</c> ("downloads arrive in") and its <c>processorOutputPath</c>. A hand-off's file
    /// has to sit inside the watched folder (<see cref="HandoffPaths.RelativeMediaPathForHandoff"/>), compared the same
    /// case- and separator-insensitive way here. The manifest publishes no download client's save folder, only the
    /// library's declared <c>downloadsPath</c> and each client's category, so a declared path inside the watched folder is
    /// reported as <see cref="SetupCheckLine.Unverified"/>, never as a pass, and each enabled client gets its own line saying
    /// the same.
    /// </summary>
    public static DelunoSetupResult EvaluateDeluno(
        string managerLabel,
        string mediaScope,
        string watchedFolder,
        string outputFolder,
        IReadOnlyList<ManagerLibraryDescriptor> libraries,
        IReadOnlyList<ManagerDownloadClientDescriptor>? downloadClients = null)
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
                "Give each its own Weir workflow."));
        }

        var downloads = WireStrings.Strip(library.DownloadsPath ?? string.Empty);
        var watched = WireStrings.Strip(watchedFolder ?? string.Empty);
        if (downloads.Length == 0)
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} does not say where {library.Name}'s downloads arrive, so Weir cannot verify that its hand-offs sit inside this workflow's watched folder."));
        }
        else if (watched.Length > 0 && Inside(downloads, watched))
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} says its {library.Name} library downloads to {downloads} (inside Weir's watched folder). " +
                "Weir can't see where each download client really saves."));
        }
        else
        {
            lines.Add(new SetupCheckLine(
                SetupCheckLine.Problem,
                $"{library.Name}'s downloads arrive in {downloads}, which is not inside the watched folder, so Weir would refuse its hand-offs. " +
                $"Use {managerLabel}'s folders."));
        }

        lines.AddRange(DownloadClientLines(managerLabel, mediaScope, downloadClients ?? []));

        var processed = WireStrings.Strip(library.OutputPath ?? string.Empty);
        var output = WireStrings.Strip(outputFolder ?? string.Empty);
        if (processed.Length > 0)
        {
            lines.Add(output.Length > 0 && Inside(processed, output) && Inside(output, processed)
                ? new SetupCheckLine(SetupCheckLine.Ok, $"{managerLabel} picks up cleaned files from {processed}, the folder this workflow writes to.")
                : new SetupCheckLine(
                    SetupCheckLine.Problem,
                    $"{managerLabel} picks up cleaned files from {processed}, but this workflow writes to {(output.Length > 0 ? output : "no output folder")}. " +
                    "Unless both are the same folder seen from two machines, use the same folder."));
        }

        return new DelunoSetupResult(downloads.Length > 0 ? downloads : null, processed.Length > 0 ? processed : null, lines);
    }

    /// <summary>One unverified line per enabled Deluno download client: the category it files this media type under, and no folder to check.</summary>
    private static IEnumerable<SetupCheckLine> DownloadClientLines(
        string managerLabel, string mediaScope, IReadOnlyList<ManagerDownloadClientDescriptor> downloadClients)
    {
        foreach (var client in downloadClients.Where(client => client.Enabled))
        {
            var category = mediaScope == MediaManagerKinds.Tv ? client.TvCategory : client.MoviesCategory;
            var filedUnder = category is null ? "its downloads" : $"the category \"{category}\"";
            yield return new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel}'s {client.Name} files this workflow's downloads under {filedUnder}, but {managerLabel} does not publish where it saves them. " +
                "Weir cannot verify they land inside the watched folder.");
        }
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
