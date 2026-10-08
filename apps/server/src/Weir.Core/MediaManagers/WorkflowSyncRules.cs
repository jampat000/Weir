using Weir.Core.Processing;

namespace Weir.Core.MediaManagers;

/// <summary>
/// One manager library that processes with Weir, as its workflow should read. <see cref="WatchedFolder"/> and
/// <see cref="OutputFolder"/> are in Weir's view and null when the manager does not say; <see cref="Problem"/>
/// is then the sentence that says what to set, and <see cref="Cause"/> says why there is none. The work folder is never part of it:
/// Weir's own. <see cref="DestinationsRead"/> is false when the manager's download destinations could not be read, so a missing
/// folder may only mean they were not seen.
/// </summary>
public sealed record SyncedLibrary(
    string Key,
    string Name,
    string MediaType,
    string? WatchedFolder,
    string? OutputFolder,
    string? Problem,
    SyncedLibraryProblem Cause = SyncedLibraryProblem.None,
    bool DestinationsRead = true);

/// <summary>Why a library has no watched folder to give its workflow.</summary>
public enum SyncedLibraryProblem
{
    None,

    /// <summary>The manager names no folder for the library's downloads.</summary>
    NoDownloadsFolder,

    /// <summary>The folder the manager names is another library's too, so it is no library's own.</summary>
    SharedDownloadsFolder,

    /// <summary>The manager names no processed folder, and a watched folder is never given without an output folder.</summary>
    NoOutputFolder,
}

/// <summary>
/// A workflow as the sync sees it. <see cref="SyncsFoldersFrom"/> is true when a manager owns its watched and output folders;
/// <see cref="LinkedConnectionIds"/> are the managers it is linked to, by the sync or by hand in the editor.
/// </summary>
public sealed record SyncWorkflow(
    long Id,
    string Name,
    string MediaType,
    string WatchedFolder,
    string OutputFolder,
    long? DiscoveredFromConnectionId,
    string? DiscoveredLibraryKey,
    bool SyncsFoldersFrom,
    IReadOnlyCollection<long> LinkedConnectionIds);

/// <summary>
/// A workflow still kept in step with a manager whose library no longer processes with Weir. <see cref="Library"/> is the library as
/// the manager lists it now, or null when the manager no longer lists it.
/// </summary>
public sealed record DepartedWorkflow(SyncWorkflow Workflow, ManagerLibraryDescriptor? Library);

public enum WorkflowSyncKind
{
    /// <summary>No workflow is linked to the library and none is free to take over, so a new one is made.</summary>
    Create,

    /// <summary>An unconfigured workflow of the same media type, such as the install default, becomes the library's.</summary>
    Adopt,

    /// <summary>The workflow already linked to the library is brought up to date.</summary>
    Update,
}

/// <summary>
/// One change the sync makes. <see cref="ChangesWatched"/> and <see cref="ChangesOutput"/> are what an <see cref="WorkflowSyncKind.Update"/> rewrites;
/// <see cref="SharedWith"/> names the workflow or library whose folder sits inside the watched folder
/// of a library the manager gives no folder of its own, which is then emptied (<see cref="ClearsWatched"/>).
/// </summary>
public sealed record WorkflowSyncAction(
    WorkflowSyncKind Kind, SyncedLibrary Library, SyncWorkflow? Workflow, bool ChangesWatched, bool ChangesOutput, string? SharedWith = null)
{
    public bool ClearsWatched => SharedWith is not null;
}

/// <summary>
/// Keeping Weir's workflows in step with a manager that reports its own folders (Deluno): which manager library gets which
/// workflow, and what changes. The watched and output folders belong to the manager; the work folder, rules profile,
/// schedule and everything else stay the person's. Nothing here deletes a workflow.
/// </summary>
public static class WorkflowSyncRules
{
    /// <summary>
    /// The manager a workflow takes its folders from: the one it was set up from, while the workflow is still linked to it and
    /// the manager reports folders. Null once it is unlinked (the sync then leaves it alone) or when it came from a manager
    /// that cannot report folders.
    /// </summary>
    public static long? FoldersSyncedFrom(
        long? discoveredFromConnectionId, string? discoveredLibraryKey, IReadOnlyCollection<long> linkedConnectionIds, string? managerKind)
    {
        ArgumentNullException.ThrowIfNull(linkedConnectionIds);
        return discoveredFromConnectionId is { } connectionId
               && !string.IsNullOrEmpty(discoveredLibraryKey)
               && linkedConnectionIds.Contains(connectionId)
               && ReportsFolders(managerKind)
            ? connectionId
            : null;
    }

    /// <summary>Whether this kind of manager says where its folders are, so its workflows are kept in step with it.</summary>
    public static bool ReportsFolders(string? managerKind) =>
        string.Equals(managerKind, "deluno", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The libraries that get a workflow: those set to Refine before import, each read with where its clients save and the
    /// path mappings when the manager published them (<paramref name="answer"/>). A library that does not process with Weir
    /// is not in the list, and a workflow already linked to it is left as it is. A downloads folder that is another listed
    /// library's too, or holds its folder, is no library's own: it is not given to the library that holds the other's, and
    /// <see cref="SyncedLibrary.Problem"/> says what to set.
    /// </summary>
    public static IReadOnlyList<SyncedLibrary> LibrariesOf(
        string managerLabel, IReadOnlyList<ManagerLibraryDescriptor> libraries, DelunoDestinationsAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(answer);
        var read = answer.Status == DelunoDestinationsStatus.Read;
        var wanted = libraries
            .Where(library => library.ProcessesBeforeImport)
            .Select(library => LibraryOf(
                managerLabel,
                library,
                read
                    ? answer.Libraries.FirstOrDefault(published => string.Equals(published.LibraryId, library.Key, StringComparison.OrdinalIgnoreCase))
                    : null) with { DestinationsRead = read })
            .ToList();
        return [.. wanted.Select(library => library.WatchedFolder is { } folder && wanted.Any(other => other.Key != library.Key && Holds(folder, other.WatchedFolder))
            ? library with
            {
                WatchedFolder = null,
                Problem = WorkflowSyncFolders.SharedDownloadsFolder(managerLabel, library.Name, folder),
                Cause = SyncedLibraryProblem.SharedDownloadsFolder,
            }
            : library)];
    }

    /// <summary>
    /// The workflows kept in step with manager <paramref name="connectionId"/> whose library is no longer listed by it, or is no
    /// longer set to Refine before import. They are reported and left as they are; a workflow that was unlinked is the person's own
    /// and is not one of them.
    /// </summary>
    public static IReadOnlyList<DepartedWorkflow> Departed(
        long connectionId, IReadOnlyList<ManagerLibraryDescriptor> libraries, IReadOnlyList<SyncWorkflow> workflows)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(workflows);
        return [.. workflows
            .Where(workflow => workflow.SyncsFoldersFrom && workflow.DiscoveredFromConnectionId == connectionId)
            .Select(workflow => new DepartedWorkflow(workflow, libraries.FirstOrDefault(library => library.Key == workflow.DiscoveredLibraryKey)))
            .Where(departed => departed.Library is not { ProcessesBeforeImport: true })];
    }

    /// <summary>
    /// A manager library that processes with Weir, as its workflow should read. A workflow is never given a watched folder
    /// without an output folder to go with it, which is what the editor refuses too, so with no output folder neither is set
    /// and <see cref="SyncedLibrary.Problem"/> says what to do.
    /// </summary>
    internal static SyncedLibrary LibraryOf(string managerLabel, ManagerLibraryDescriptor library, DelunoLibraryDestinations? destinations)
    {
        ArgumentNullException.ThrowIfNull(library);
        var mediaType = ProcessingMediaScopes.Normalize(library.MediaScope);
        var watched = WorkflowSyncFolders.Watched(managerLabel, library.Name, library.DownloadsPath, destinations);
        var output = WorkflowSyncFolders.Output(library.OutputPath ?? destinations?.ProcessorOutputPath, destinations);
        if (output is null)
        {
            return watched.Problem is null
                ? new SyncedLibrary(library.Key, library.Name, mediaType, null, null, WorkflowSyncFolders.NoOutputFolder(managerLabel, library.Name), SyncedLibraryProblem.NoOutputFolder)
                : new SyncedLibrary(library.Key, library.Name, mediaType, null, null, watched.Problem, SyncedLibraryProblem.NoDownloadsFolder);
        }

        return new SyncedLibrary(
            library.Key, library.Name, mediaType, watched.Folder, output, watched.Problem, watched.Problem is null ? SyncedLibraryProblem.None : SyncedLibraryProblem.NoDownloadsFolder);
    }

    /// <summary>
    /// What to do for each library of manager <paramref name="connectionId"/>, in the order given. A library whose workflow was
    /// unlinked from it is left alone. A library with no workflow of its own takes, in this order, a workflow of its media type
    /// that someone linked to this manager by hand with exactly the library's folders, then an unconfigured one (no watched or output folder, not linked); each is
    /// adopted at most once, first in <paramref name="workflows"/>' order. An update that would change nothing is not an action.
    /// </summary>
    public static IReadOnlyList<WorkflowSyncAction> Plan(
        long connectionId, IReadOnlyList<SyncedLibrary> libraries, IReadOnlyList<SyncWorkflow> workflows)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        ArgumentNullException.ThrowIfNull(workflows);
        var actions = new List<WorkflowSyncAction>();
        var adopted = new HashSet<long>();
        foreach (var library in libraries)
        {
            var linked = workflows.FirstOrDefault(workflow =>
                workflow.DiscoveredFromConnectionId == connectionId && workflow.DiscoveredLibraryKey == library.Key);
            if (linked is not null)
            {
                if (linked.SyncsFoldersFrom && Update(library, linked, libraries, workflows) is { } update)
                {
                    actions.Add(update);
                }

                continue;
            }

            var free = workflows.FirstOrDefault(workflow =>
                           !adopted.Contains(workflow.Id) && IsLinkedByHandTo(workflow, connectionId, library) && workflow.MediaType == library.MediaType)
                       ?? workflows.FirstOrDefault(workflow =>
                           !adopted.Contains(workflow.Id) && IsUnconfigured(workflow) && workflow.MediaType == library.MediaType);
            if (free is not null)
            {
                adopted.Add(free.Id);
                actions.Add(new WorkflowSyncAction(WorkflowSyncKind.Adopt, library, free, library.WatchedFolder is not null, library.OutputFolder is not null));
                continue;
            }

            actions.Add(new WorkflowSyncAction(WorkflowSyncKind.Create, library, null, library.WatchedFolder is not null, library.OutputFolder is not null));
        }

        return actions;
    }

    /// <summary>
    /// Linked to this manager in the editor, not set up from any of its libraries, and already holding exactly the folders this
    /// library reports: the sync takes it over (which changes nothing on disk) rather than making a second one. A linked workflow
    /// with other folders may have been taken out of the sync on purpose, so it is left alone.
    /// </summary>
    private static bool IsLinkedByHandTo(SyncWorkflow workflow, long connectionId, SyncedLibrary library) =>
        workflow.DiscoveredFromConnectionId is null
        && workflow.LinkedConnectionIds.Contains(connectionId)
        && library.WatchedFolder is { } watched && SameFolder(watched, workflow.WatchedFolder)
        && library.OutputFolder is { } output && SameFolder(output, workflow.OutputFolder);

    /// <summary>A workflow nobody has set up: no watched folder, no output folder, and not linked to a manager library.</summary>
    private static bool IsUnconfigured(SyncWorkflow workflow) =>
        workflow.DiscoveredFromConnectionId is null
        && string.IsNullOrWhiteSpace(workflow.WatchedFolder)
        && string.IsNullOrWhiteSpace(workflow.OutputFolder);

    /// <summary>
    /// A manager that gives a library no folder of its own leaves the workflow's watched folder as it is, unless that folder is
    /// the same as, or holds, another workflow's watched folder or the folder the manager gives another library. A folder that
    /// holds another's is no one workflow's own, and the manager's folders never overlap, so it is cleared; the folder inside it
    /// is kept. A folder is cleared only when the manager was heard to name none: when its download destinations could not be read,
    /// a missing folder may only mean they were not seen.
    /// </summary>
    private static WorkflowSyncAction? Update(
        SyncedLibrary library, SyncWorkflow workflow, IReadOnlyList<SyncedLibrary> libraries, IReadOnlyList<SyncWorkflow> workflows)
    {
        var watched = library.WatchedFolder is { } wantedWatched && !SameFolder(wantedWatched, workflow.WatchedFolder);
        var output = library.OutputFolder is { } wantedOutput && !SameFolder(wantedOutput, workflow.OutputFolder);
        var sharedWith = library.WatchedFolder is null && (library.DestinationsRead || library.Cause == SyncedLibraryProblem.SharedDownloadsFolder)
            ? HolderOf(workflow, library, libraries, workflows)
            : null;
        return watched || output || sharedWith is not null
            ? new WorkflowSyncAction(WorkflowSyncKind.Update, library, workflow, watched, output, sharedWith)
            : null;
    }

    /// <summary>The name of the first other workflow, or other library the manager gives a folder, that <paramref name="workflow"/>'s watched folder is or holds.</summary>
    private static string? HolderOf(
        SyncWorkflow workflow, SyncedLibrary library, IReadOnlyList<SyncedLibrary> libraries, IReadOnlyList<SyncWorkflow> workflows) =>
        workflows.FirstOrDefault(other => other.Id != workflow.Id && Holds(workflow.WatchedFolder, other.WatchedFolder))?.Name
        ?? libraries.FirstOrDefault(other => other.Key != library.Key && Holds(workflow.WatchedFolder, other.WatchedFolder))?.Name;

    /// <summary>Whether <paramref name="folder"/> is <paramref name="other"/> or has it inside it; false when either is blank.</summary>
    private static bool Holds(string folder, string? other) =>
        LibraryRules.NormalizeFolder(folder) is { } held
        && LibraryRules.NormalizeFolder(other) is { } inner
        && (held == inner || LibraryRules.IsAncestor(held, inner));

    /// <summary>The same folder, written either way round: case- and separator-insensitive, as a hand-off's path is compared.</summary>
    private static bool SameFolder(string first, string second) =>
        LibraryDiscoveryRules.Comparable(first) == LibraryDiscoveryRules.Comparable(second);

    public static string DepartedTitle(DepartedWorkflow departed, string managerLabel) =>
        departed.Library is { } library
            ? $"{managerLabel} no longer hands {library.Name} to Weir"
            : $"{managerLabel} no longer has the library for {departed.Workflow.Name}";

    /// <summary>Why the workflow is not being worked on any more, and that Weir has left it as it is.</summary>
    public static string DepartedMessage(DepartedWorkflow departed, string managerLabel) =>
        (departed.Library is { } library
            ? DelunoDestinationNotices.StoppedRefining(managerLabel, library.Name, library.MediaScope)
            : $"{DelunoDestinationNotices.LibraryGoneFact(managerLabel)}.")
        + $" Weir left {departed.Workflow.Name} as it is.";

    public static string DepartedNextAction(DepartedWorkflow departed, string managerLabel) =>
        departed.Library is null
            ? DelunoDestinationNotices.LibraryGoneAdvice(managerLabel)
            : $"{DelunoDestinationNotices.StoppedRefiningAdvice(managerLabel)} If you no longer want {departed.Workflow.Name}, unlink it from {managerLabel} or remove it in Weir.";

    public static string SetUpTitle(string workflowName, string managerLabel) => $"Workflow {workflowName} set up from {managerLabel}";

    public static string UpdatedTitle(string workflowName, string managerLabel, bool watched, bool output)
    {
        var which = watched && output ? "watched and output folders" : watched ? "watched folder" : "output folder";
        return $"{Possessive(workflowName)} {which} updated from {managerLabel}";
    }

    public static string ClearedTitle(string workflowName) => $"{Possessive(workflowName)} watched folder cleared";

    /// <summary>
    /// Why the watched folder was emptied and what to set so Weir fills it in again: <paramref name="problem"/> is the sentence
    /// for why the manager gives the library no folder of its own (nothing named, a folder another library shares, no processed folder).
    /// </summary>
    public static string ClearedMessage(string workflowName, string cleared, string sharedWith, string problem) =>
        $"Weir cleared {Possessive(workflowName)} watched folder, {cleared}, because it is the same as, or holds, the folder of {sharedWith}. {problem}";

    /// <summary>What a library with no watched folder to give is called in Activity.</summary>
    public static string ProblemTitle(SyncedLibrary library, string managerLabel) =>
        library.Cause == SyncedLibraryProblem.SharedDownloadsFolder
            ? $"{managerLabel} gives {library.Name} a downloads folder that another library uses too"
            : $"{managerLabel} has not told Weir all of the folders for {library.Name} yet";

    public static string ProblemNextAction(SyncedLibrary library, string managerLabel) =>
        library.Cause == SyncedLibraryProblem.SharedDownloadsFolder
            ? $"Give {library.Name} its own downloads folder in {managerLabel}."
            : $"Set the folder in {managerLabel}.";

    /// <summary>"Movies'" and "Kids'" after an s, "TV's" after anything else.</summary>
    internal static string Possessive(string name) => name.EndsWith('s') ? name + "'" : name + "'s";

    /// <summary>The sentence under an event's title, saying what Weir did and what still belongs to the person.</summary>
    public static string SetUpMessage(string workflowName, string managerLabel, string? watched, string? output) =>
        $"Weir set up {workflowName} from {managerLabel}: " +
        $"it watches {watched ?? "no folder yet"} and writes to {output ?? "no folder yet"}. " +
        $"{managerLabel} keeps those two folders up to date; the work folder and everything else stay yours to change.";

    public static string UpdatedMessage(string workflowName, string managerLabel, string? watched, string? output) =>
        $"{managerLabel} now reports {FoldersPhrase(watched, output)} for {workflowName}, so Weir changed it to match.";

    private static string FoldersPhrase(string? watched, string? output) =>
        watched is not null && output is not null
            ? $"{watched} for downloads and {output} for cleaned files"
            : watched is not null ? $"{watched} for downloads" : $"{output} for cleaned files";
}
