using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

public static partial class ManagerSetupRules
{
    /// <summary>
    /// Deluno hands each finished download to Weir over its API and imports the result itself, translating paths with its
    /// own path mappings on its side. What Weir can read reliably from its manifest (<c>GET /api/integrations/external/manifest</c>)
    /// is each library's workflow, its <c>downloadsPath</c> ("downloads arrive in") and its <c>processorOutputPath</c>;
    /// the mappings are not in it. A hand-off's file has to sit inside the watched folder
    /// (<see cref="HandoffPaths.RelativeMediaPathForHandoff"/>), compared the same case- and separator-insensitive way
    /// here and, when <paramref name="probe"/> is given, after following any junction or link, so one folder reached two
    /// ways is one folder. A reported folder that is still not Weir's cannot be told apart from one Deluno maps, so it is
    /// <see cref="SetupCheckLine.Unverified"/> with both ways to fix it, never a pass and never a failure. The manifest
    /// publishes no download client's save folder, only the library's declared <c>downloadsPath</c> and each client's
    /// category, so a declared path inside the watched folder is reported as <see cref="SetupCheckLine.Unverified"/>,
    /// never as a pass, and each enabled client gets its own line saying the same.
    /// </summary>
    public static DelunoSetupResult EvaluateDeluno(
        string managerLabel,
        string mediaScope,
        string watchedFolder,
        string outputFolder,
        IReadOnlyList<ManagerLibraryDescriptor> libraries,
        IReadOnlyList<ManagerDownloadClientDescriptor>? downloadClients = null,
        IFolderProbe? probe = null)
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
        lines.Add(DownloadsLine(managerLabel, library.Name, downloads, watched, probe));
        lines.AddRange(DownloadClientLines(managerLabel, mediaScope, downloadClients ?? []));

        var processed = WireStrings.Strip(library.OutputPath ?? string.Empty);
        var output = WireStrings.Strip(outputFolder ?? string.Empty);
        if (processed.Length > 0)
        {
            lines.Add(OutputLine(managerLabel, processed, output, probe));
        }

        return new DelunoSetupResult(downloads.Length > 0 ? downloads : null, processed.Length > 0 ? processed : null, lines);
    }

    /// <summary>Whether the folder Deluno says downloads arrive in is inside Weir's watched folder, and what to do when it is not.</summary>
    private static SetupCheckLine DownloadsLine(string managerLabel, string libraryName, string downloads, string watched, IFolderProbe? probe)
    {
        if (downloads.Length == 0)
        {
            return new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} does not say where {libraryName}'s downloads arrive, so Weir cannot verify that its hand-offs sit inside this workflow's watched folder.");
        }

        if (watched.Length == 0)
        {
            return new SetupCheckLine(
                SetupCheckLine.Problem,
                $"{managerLabel} reports {libraryName}'s downloads in {downloads}, but this workflow has no watched folder. Set the watched folder to {downloads}.");
        }

        const string NoClientFolder = "Weir can't see where each download client really saves.";
        if (Inside(downloads, watched))
        {
            return new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} says its {libraryName} library downloads to {downloads} (inside Weir's watched folder). {NoClientFolder}");
        }

        return InsideOnceLinksAreFollowed(downloads, watched, probe)
            ? new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} says its {libraryName} library downloads to {downloads}, which leads into Weir's watched folder {watched}. {NoClientFolder}")
            : new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} reports {libraryName}'s downloads in {downloads}, which isn't inside this workflow's watched folder {watched} as Weir sees it. " +
                DelunoPathMappingAdvice.For(managerLabel, downloads, watched, "the watched folder"));
    }

    /// <summary>Whether the folder Deluno picks cleaned files up from is Weir's output folder, and what to do when it is not.</summary>
    private static SetupCheckLine OutputLine(string managerLabel, string processed, string output, IFolderProbe? probe)
    {
        if (output.Length == 0)
        {
            return new SetupCheckLine(
                SetupCheckLine.Problem,
                $"{managerLabel} picks up cleaned files from {processed}, but this workflow has no output folder. Set the output folder to {processed}.");
        }

        if (SameFolder(processed, output))
        {
            return new SetupCheckLine(SetupCheckLine.Ok, $"{managerLabel} picks up cleaned files from {processed}, the folder this workflow writes to.");
        }

        return SameOnceLinksAreFollowed(processed, output, probe)
            ? new SetupCheckLine(SetupCheckLine.Ok, $"{managerLabel} picks up cleaned files from {processed}, which leads to the folder this workflow writes to, {output}.")
            : new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{managerLabel} picks up cleaned files from {processed}, which isn't this workflow's output folder {output} as Weir sees it. " +
                DelunoPathMappingAdvice.For(managerLabel, processed, output, "the output folder"));
    }

    private static bool InsideOnceLinksAreFollowed(string path, string folder, IFolderProbe? probe) =>
        probe?.ResolveFinalPath(path) is { } finalPath && probe.ResolveFinalPath(folder) is { } finalFolder && Inside(finalPath, finalFolder);

    private static bool SameOnceLinksAreFollowed(string first, string second, IFolderProbe? probe) =>
        probe?.ResolveFinalPath(first) is { } finalFirst && probe.ResolveFinalPath(second) is { } finalSecond && SameFolder(finalFirst, finalSecond);

    private static bool SameFolder(string first, string second) => Inside(first, second) && Inside(second, first);

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
