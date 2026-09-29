namespace Weir.Core.MediaManagers;

/// <summary>
/// The filesystem seam <see cref="LibraryFolderChainRules"/> checks through, so the rule itself stays pure and testable.
/// A real implementation lives in <c>Weir.Infrastructure.MediaManagers.FilesystemFolderProbe</c>.
/// </summary>
public interface IFolderProbe
{
    /// <summary>Whether the folder exists.</summary>
    bool Exists(string path);

    /// <summary>Whether Weir can read the folder's contents. Only meaningful when it exists.</summary>
    bool CanRead(string path);

    /// <summary>Whether Weir can write into the folder. Only meaningful when it exists.</summary>
    bool CanWrite(string path);

    /// <summary>
    /// True or false when Weir can tell whether the two folders share a filesystem — the condition for a finished file to
    /// be moved into place rather than copied — and null when it cannot tell.
    /// </summary>
    bool? SameFilesystem(string first, string second);
}

/// <summary>
/// Weir's own side of a library's folder chain: whether the watched, work and output folders exist and are
/// usable, and whether a finished file can be moved from the work folder into the output folder or has to be copied
/// across. Pure: nothing here touches the filesystem — <see cref="IFolderProbe"/> is the seam a caller fills with a real
/// or fake implementation. The media-manager half of the chain is unchanged — <see cref="ManagerSetupRules"/>, reached
/// through <c>ManagerSetupCheck</c>.
/// </summary>
public static class LibraryFolderChainRules
{
    /// <summary>
    /// Checks the watched, work and output folders in order. A blank folder produces one combined line rather than one
    /// per missing folder (the library is still being set up); otherwise each folder gets its own existence/access line,
    /// and the work and output folders also get a line about whether a finished file can move between them.
    /// </summary>
    public static IReadOnlyList<SetupCheckLine> CheckLocalFolders(
        string watchedFolder, string workFolder, bool workFolderIsDefault, string outputFolder, IFolderProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var watched = (watchedFolder ?? string.Empty).Trim();
        var work = (workFolder ?? string.Empty).Trim();
        var output = (outputFolder ?? string.Empty).Trim();

        if (watched.Length == 0 || work.Length == 0 || output.Length == 0)
        {
            return [new SetupCheckLine(SetupCheckLine.Problem, BlankFolderLine(watched.Length == 0, work.Length == 0, output.Length == 0))];
        }

        var lines = new List<SetupCheckLine>();
        lines.Add(FolderReadLine("watched", watched, probe));
        lines.Add(WorkFolderExistsLine(work, workFolderIsDefault, probe));
        lines.Add(FolderWriteLine("output", output, probe));
        lines.Add(SameFilesystemLine(work, output, probe));
        return lines;
    }

    /// <summary>
    /// One combined line, naming the single folder that is missing when only one is, or all three by name otherwise —
    /// the same economy of words as <c>ManagerSetupRules.EvaluateArr</c>'s early return.
    /// </summary>
    private static string BlankFolderLine(bool watchedMissing, bool workMissing, bool outputMissing)
    {
        var missingCount = (watchedMissing ? 1 : 0) + (workMissing ? 1 : 0) + (outputMissing ? 1 : 0);
        if (missingCount > 1)
        {
            return "Set this library's watched, work and output folders so Weir can check its folder chain.";
        }

        if (watchedMissing)
        {
            return "Set a watched folder so Weir has somewhere to look for files.";
        }

        return workMissing
            ? "Set a work folder, or leave it blank to use Weir's own default, so Weir has somewhere to stage files while it cleans them."
            : "Set an output folder so Weir has somewhere to write cleaned files.";
    }

    private static SetupCheckLine FolderReadLine(string label, string folder, IFolderProbe probe)
    {
        if (!probe.Exists(folder))
        {
            return new SetupCheckLine(SetupCheckLine.Problem, $"The {label} folder {folder} does not exist. Create it, or point this library at a folder that does.");
        }

        return probe.CanRead(folder)
            ? new SetupCheckLine(SetupCheckLine.Ok, $"Weir can read the {label} folder {folder}.")
            : new SetupCheckLine(SetupCheckLine.Problem, $"Weir cannot read the {label} folder {folder}. Check its permissions, or point this library at a folder Weir can read.");
    }

    private static SetupCheckLine FolderWriteLine(string label, string folder, IFolderProbe probe)
    {
        if (!probe.Exists(folder))
        {
            return new SetupCheckLine(SetupCheckLine.Problem, $"The {label} folder {folder} does not exist. Create it, or point this library at a folder that does.");
        }

        return probe.CanWrite(folder)
            ? new SetupCheckLine(SetupCheckLine.Ok, $"Weir can write to the {label} folder {folder}.")
            : new SetupCheckLine(SetupCheckLine.Problem, $"Weir cannot write to the {label} folder {folder}. Check its permissions, or point this library at a folder Weir can write to.");
    }

    /// <summary>
    /// The work folder's own existence line: a custom work folder must exist like any other, but the default one (under
    /// Weir's home) is created the first time it is needed, so it not existing yet is not a problem.
    /// </summary>
    private static SetupCheckLine WorkFolderExistsLine(string work, bool workFolderIsDefault, IFolderProbe probe)
    {
        if (probe.Exists(work))
        {
            return new SetupCheckLine(SetupCheckLine.Ok, $"Weir can use the work folder {work}.");
        }

        return workFolderIsDefault
            ? new SetupCheckLine(SetupCheckLine.Note, $"Weir will create the work folder {work} the first time it processes a file for this library.")
            : new SetupCheckLine(SetupCheckLine.Problem, $"The work folder {work} does not exist. Create it, or point this library's work folder at one that does.");
    }

    /// <summary>
    /// Where a bare download client really saves, read from the client itself, against this library's watched folder. Each
    /// folder the client reports (its default completed-downloads folder, then each category's own) gets a line: Ok when it
    /// is inside the watched folder, a note when it is elsewhere but another folder of the client is inside, and, when none
    /// is, one problem naming every folder the client saves to. Uses <see cref="ArrOsPath.Contains"/> since these are plain
    /// local paths on Weir's own host, never remote-mapped the way a manager's are. A client that reported no folder at all
    /// is <see cref="SetupCheckLine.Unverified"/>.
    /// </summary>
    public static IReadOnlyList<SetupCheckLine> CheckDownloadClientFolders(string clientLabel, string watchedFolder, DownloadClientFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var watched = new ArrOsPath((watchedFolder ?? string.Empty).Trim());
        if (!watched.IsRooted)
        {
            return [new SetupCheckLine(SetupCheckLine.Unverified, $"Set this library's watched folder to check it against {clientLabel}.")];
        }

        var saveFolders = SaveFolders(folders);
        if (saveFolders.Count == 0)
        {
            return [new SetupCheckLine(
                SetupCheckLine.Unverified,
                $"{clientLabel} did not say where it saves, so Weir cannot verify that downloads land in {watched}. Check it is running and its saved address and login are right.")];
        }

        var isInside = saveFolders.ConvertAll(saveFolder => watched.Contains(new ArrOsPath(saveFolder.Folder)));
        if (!isInside.Contains(true))
        {
            var actual = string.Join("; ", saveFolders.Select(saveFolder => $"{saveFolder.Name} {saveFolder.Folder}"));
            return [new SetupCheckLine(
                SetupCheckLine.Problem,
                $"{clientLabel} saves to: {actual}. None of that is inside this library's watched folder {watched}, so Weir would never see the downloads. " +
                "Point the client's folder at it, or use the suggested folder in the library editor.")];
        }

        return [.. saveFolders.Select((saveFolder, index) => isInside[index]
            ? new SetupCheckLine(SetupCheckLine.Ok, $"{clientLabel}'s {saveFolder.Name} is {saveFolder.Folder}, inside this library's watched folder.")
            : new SetupCheckLine(SetupCheckLine.Note, $"{clientLabel}'s {saveFolder.Name} is {saveFolder.Folder}, outside this library's watched folder. That is fine when it belongs to another library."))];
    }

    /// <summary>The client's default folder, then each category's own.</summary>
    private static List<ClientSaveFolder> SaveFolders(DownloadClientFolders folders)
    {
        var result = new List<ClientSaveFolder>();
        if (!string.IsNullOrEmpty(folders.CompletedFolder))
        {
            result.Add(new ClientSaveFolder("default completed-downloads folder", folders.CompletedFolder));
        }

        result.AddRange(folders.CategoryFolders
            .Where(category => !string.IsNullOrEmpty(category.Folder))
            .Select(category => new ClientSaveFolder($"\"{category.Category}\" category folder", category.Folder)));
        return result;
    }

    /// <summary>One folder a download client saves to, and the words that say which of its folders it is.</summary>
    private readonly record struct ClientSaveFolder(string Name, string Folder);

    /// <summary>
    /// Whether a finished file moves from the work folder into the output folder or has to be copied. Either is a valid
    /// setup — a different drive just costs the time a copy takes on a large file — so this is never a problem, only an
    /// Ok when they match or a Note explaining the copy when they do not (or when Weir cannot tell).
    /// </summary>
    private static SetupCheckLine SameFilesystemLine(string work, string output, IFolderProbe probe)
    {
        var same = probe.SameFilesystem(work, output);
        if (same == true)
        {
            return new SetupCheckLine(SetupCheckLine.Ok, "The work folder and output folder are on the same drive, so a finished file is moved into place instantly.");
        }

        return same == false
            ? new SetupCheckLine(SetupCheckLine.Note, "The work folder and output folder are on different drives. That's fine — Weir will copy each finished file across instead of moving it, which takes longer for a large file.")
            : new SetupCheckLine(SetupCheckLine.Note, "Weir could not tell whether the work folder and output folder are on the same drive.");
    }
}
