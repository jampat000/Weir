using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class WorkflowFoldersTests
{
    private static readonly string Home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "weir-home"));

    private static string Full(string name) => Path.GetFullPath(Path.Combine(Path.GetTempPath(), name));

    private static ProcessingLibraryRecord Workflow(long id, string name, string watched, string work, string output, long keepFreeMb = 5120) => new()
    {
        Id = id,
        Name = name,
        WatchedFolder = watched,
        WorkFolder = work,
        OutputFolder = output,
        MinimumFreeDiskSpaceMb = keepFreeMb,
    };

    [Fact]
    public void A_workflow_has_a_watched_a_work_and_an_output_folder_each_keeping_its_free_space_wish()
    {
        var movies = Workflow(1, "Movies", Full("in"), Full("work"), Full("out"), keepFreeMb: 2048);

        var folders = WorkflowFolders.Of([movies], Home);

        Assert.Equal(
            [
                new WorkflowFolder(1, "Movies", WorkflowFolders.Watched, Full("in"), 2048L * 1024 * 1024),
                new WorkflowFolder(1, "Movies", WorkflowFolders.Work, Full("work"), 2048L * 1024 * 1024),
                new WorkflowFolder(1, "Movies", WorkflowFolders.Output, Full("out"), 2048L * 1024 * 1024),
            ],
            folders);
    }

    [Fact]
    public void A_workflow_without_a_work_folder_of_its_own_uses_the_one_under_weirs_home()
    {
        var folders = WorkflowFolders.Of([Workflow(1, "Movies", Full("in"), string.Empty, Full("out"))], Home);

        Assert.Equal(
            ProcessingLibraryFolders.DefaultMovieWorkFolder(Home),
            folders.Single(folder => folder.Role == WorkflowFolders.Work).Path);
    }

    [Fact]
    public void A_folder_that_is_not_set_is_left_out()
    {
        var folders = WorkflowFolders.Of([Workflow(1, "Movies", string.Empty, Full("work"), "  ")], Home);

        Assert.Equal([WorkflowFolders.Work], folders.Select(folder => folder.Role));
    }

    [Fact]
    public void A_path_the_system_cannot_make_sense_of_belongs_to_no_drive()
    {
        var folders = WorkflowFolders.Of([Workflow(1, "Movies", "bad\0path", Full("work"), Full("out"))], Home);

        Assert.DoesNotContain(folders, folder => folder.Role == WorkflowFolders.Watched);
    }
}
