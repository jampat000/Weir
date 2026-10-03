using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>One folder a workflow uses, the part it plays, and how much free space the workflow wants kept on its drive.</summary>
public sealed record WorkflowFolder(long WorkflowId, string WorkflowName, string Role, string Path, long KeepFreeBytes);

/// <summary>The folders every workflow uses: where it watches, where it works and where it writes.</summary>
public static class WorkflowFolders
{
    public const string Watched = "watched";
    public const string Work = "work";
    public const string Output = "output";

    private const long BytesPerMegabyte = 1024 * 1024;

    /// <summary>Every workflow's watched, work and output folders as full paths. A workflow with no work folder of its own uses the one under <paramref name="weirHome"/>.</summary>
    public static IReadOnlyList<WorkflowFolder> Of(IEnumerable<ProcessingLibraryRecord> workflows, string weirHome)
    {
        var folders = new List<WorkflowFolder>();
        foreach (var workflow in workflows)
        {
            var folderRow = new ProcessingLibraryFolderRow(workflow.Id, workflow.MediaType, (int)workflow.DisplayOrder, workflow.WorkFolder, workflow.OutputFolder);
            var keepFree = Math.Max(0, workflow.MinimumFreeDiskSpaceMb) * BytesPerMegabyte;
            AddIfUsable(folders, workflow, Watched, workflow.WatchedFolder, keepFree);
            AddIfUsable(folders, workflow, Work, ProcessingLibraryFolders.EffectiveWorkFolder(folderRow, weirHome), keepFree);
            AddIfUsable(folders, workflow, Output, workflow.OutputFolder, keepFree);
        }

        return folders;
    }

    private static void AddIfUsable(List<WorkflowFolder> folders, ProcessingLibraryRecord workflow, string role, string path, long keepFree)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            folders.Add(new WorkflowFolder(workflow.Id, workflow.Name, role, ProcessingLibraryFolders.ExpandForFilesystem(path.Trim()), keepFree));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the system cannot make sense of belongs to no drive.
        }
    }
}
