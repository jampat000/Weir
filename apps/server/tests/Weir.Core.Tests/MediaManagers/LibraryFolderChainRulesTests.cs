using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary>
/// <see cref="LibraryFolderChainRules"/>: Weir's own watched/work/output folder half of #768's chain check, against a
/// fake <see cref="IFolderProbe"/> so no test touches the real filesystem.
/// </summary>
public sealed class LibraryFolderChainRulesTests
{
    private sealed class FakeFolderProbe : IFolderProbe
    {
        public HashSet<string> Existing { get; } = [];
        public HashSet<string> Unreadable { get; } = [];
        public HashSet<string> Unwritable { get; } = [];
        public bool? SameFilesystemAnswer { get; set; } = true;

        public bool Exists(string path) => Existing.Contains(path);

        public bool CanRead(string path) => !Unreadable.Contains(path);

        public bool CanWrite(string path) => !Unwritable.Contains(path);

        public bool? SameFilesystem(string first, string second) => SameFilesystemAnswer;
    }

    private const string Watched = "/media/watched";
    private const string Work = "/media/work";
    private const string Output = "/media/output";

    private static FakeFolderProbe AllFoldersExist() => new()
    {
        Existing = { Watched, Work, Output },
    };

    private static IReadOnlyList<SetupCheckLine> Check(FakeFolderProbe probe, bool workIsDefault = false, string? watched = Watched, string? work = Work, string? output = Output) =>
        LibraryFolderChainRules.CheckLocalFolders(watched ?? string.Empty, work ?? string.Empty, workIsDefault, output ?? string.Empty, probe);

    [Fact]
    public void Every_folder_existing_and_usable_and_sharing_a_drive_is_all_ok()
    {
        var lines = Check(AllFoldersExist());

        Assert.All(lines, line => Assert.Equal(SetupCheckLine.Ok, line.State));
        Assert.Contains(lines, line => line.Text.Contains("can read the watched folder", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Text.Contains("can use the work folder", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Text.Contains("can write to the output folder", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Text.Contains("moved into place instantly", StringComparison.Ordinal));
    }

    [Fact]
    public void A_watched_folder_that_does_not_exist_is_a_problem_naming_it()
    {
        var probe = AllFoldersExist();
        probe.Existing.Remove(Watched);

        var lines = Check(probe);

        var problem = Assert.Single(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains(Watched, problem.Text, StringComparison.Ordinal);
        Assert.Contains("does not exist", problem.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_watched_folder_weir_cannot_read_is_a_problem()
    {
        var probe = AllFoldersExist();
        probe.Unreadable.Add(Watched);

        var lines = Check(probe);

        var problem = Assert.Single(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains("cannot read", problem.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_folder_that_does_not_exist_is_a_problem()
    {
        var probe = AllFoldersExist();
        probe.Existing.Remove(Output);

        var lines = Check(probe);

        var problem = Assert.Single(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains(Output, problem.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_folder_weir_cannot_write_to_is_a_problem()
    {
        var probe = AllFoldersExist();
        probe.Unwritable.Add(Output);

        var lines = Check(probe);

        var problem = Assert.Single(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains("cannot write", problem.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_custom_work_folder_is_a_problem()
    {
        var probe = AllFoldersExist();
        probe.Existing.Remove(Work);

        var lines = Check(probe, workIsDefault: false);

        var problem = Assert.Single(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains(Work, problem.Text, StringComparison.Ordinal);
        Assert.Contains("does not exist", problem.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_default_work_folder_is_only_a_note_that_weir_will_create_it()
    {
        var probe = AllFoldersExist();
        probe.Existing.Remove(Work);

        var lines = Check(probe, workIsDefault: true);

        Assert.DoesNotContain(lines, line => line.State == SetupCheckLine.Problem);
        var note = Assert.Single(lines, line => line.Text.Contains("will create the work folder", StringComparison.Ordinal));
        Assert.Equal(SetupCheckLine.Note, note.State);
    }

    [Fact]
    public void Different_drives_for_work_and_output_are_a_note_not_a_problem()
    {
        var probe = AllFoldersExist();
        probe.SameFilesystemAnswer = false;

        var lines = Check(probe);

        Assert.DoesNotContain(lines, line => line.State == SetupCheckLine.Problem);
        var note = Assert.Single(lines, line => line.Text.Contains("different drives", StringComparison.Ordinal));
        Assert.Equal(SetupCheckLine.Note, note.State);
        Assert.Contains("copy each finished file", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_same_drive_answer_is_a_note_saying_weir_could_not_tell()
    {
        var probe = AllFoldersExist();
        probe.SameFilesystemAnswer = null;

        var lines = Check(probe);

        Assert.DoesNotContain(lines, line => line.State == SetupCheckLine.Problem);
        Assert.Contains(lines, line => line.Text.Contains("could not tell", StringComparison.Ordinal));
    }

    [Fact]
    public void One_blank_folder_gets_a_single_line_naming_that_folder()
    {
        var lines = Check(AllFoldersExist(), output: "");

        var line = Assert.Single(lines);
        Assert.Equal(SetupCheckLine.Problem, line.State);
        Assert.Contains("output folder", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_blank_folders_get_one_combined_line_not_one_each()
    {
        var lines = Check(AllFoldersExist(), watched: "", output: "");

        var line = Assert.Single(lines);
        Assert.Equal(SetupCheckLine.Problem, line.State);
        Assert.Contains("watched, work and output folders", line.Text, StringComparison.Ordinal);
    }


    private static IReadOnlyList<SetupCheckLine> CheckClient(string label, string watched, string? completed, params (string Category, string Folder)[] categories) =>
        LibraryFolderChainRules.CheckDownloadClientFolders(
            label, watched, new DownloadClientFolders(completed, [.. categories.Select(item => new DownloadClientCategoryFolder(item.Category, item.Folder))]));

    [Fact]
    public void A_download_clients_completed_folder_matching_the_watched_folder_is_ok()
    {
        var line = Assert.Single(CheckClient("qBittorrent", Watched, Watched));

        Assert.Equal(SetupCheckLine.Ok, line.State);
        Assert.Equal("qBittorrent's default completed-downloads folder is /media/watched, inside this workflow's watched folder.", line.Text);
    }

    [Fact]
    public void A_download_client_saving_to_a_folder_inside_the_watched_folder_is_ok()
    {
        var line = Assert.Single(CheckClient("SABnzbd", Watched, null, ("movies", Watched + "/Blade Runner")));

        Assert.Equal(SetupCheckLine.Ok, line.State);
        Assert.Contains("\"movies\" category folder is /media/watched/Blade Runner", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_download_client_saving_above_the_watched_folder_is_a_problem_naming_both_folders()
    {
        var line = Assert.Single(CheckClient("Transmission", @"C:\Downloads\Completed\Movies", @"C:\Downloads\Completed"));

        Assert.Equal(SetupCheckLine.Problem, line.State);
        Assert.Equal(
            @"Transmission saves to: default completed-downloads folder C:\Downloads\Completed. " +
            @"None of that is inside this workflow's watched folder C:\Downloads\Completed\Movies, so Weir would never see the downloads. " +
            "Point the client's folder at it, or use the suggested folder in the workflow editor.",
            line.Text);
    }

    [Fact]
    public void A_download_client_with_no_folder_inside_the_watched_folder_is_one_problem_naming_every_folder_it_saves_to()
    {
        var line = Assert.Single(CheckClient("NZBGet", Watched, "/downloads/complete", ("movies", "/downloads/complete/movies"), ("tv", "/downloads/complete/tv")));

        Assert.Equal(SetupCheckLine.Problem, line.State);
        Assert.Contains("default completed-downloads folder /downloads/complete", line.Text, StringComparison.Ordinal);
        Assert.Contains("\"movies\" category folder /downloads/complete/movies", line.Text, StringComparison.Ordinal);
        Assert.Contains("\"tv\" category folder /downloads/complete/tv", line.Text, StringComparison.Ordinal);
        Assert.Contains(Watched, line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_folder_of_a_download_client_gets_its_own_line_and_one_folder_inside_is_enough()
    {
        var lines = CheckClient("SABnzbd", Watched, "/downloads/complete", ("tv-sonarr", Watched), ("movies", "/downloads/complete/movies"));

        Assert.Equal(
            [SetupCheckLine.Note, SetupCheckLine.Ok, SetupCheckLine.Note],
            lines.Select(line => line.State));
        Assert.Contains("\"tv-sonarr\" category folder is /media/watched, inside", lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("outside this workflow's watched folder", lines[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_download_client_that_reported_no_folder_is_unverified_not_ok()
    {
        var line = Assert.Single(LibraryFolderChainRules.CheckDownloadClientFolders("Deluge", Watched, DownloadClientFolders.Empty));

        Assert.Equal(SetupCheckLine.Unverified, line.State);
        Assert.Contains("did not say where it saves", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_watched_folder_leaves_a_download_client_unverified()
    {
        var line = Assert.Single(CheckClient("Deluge", "", null));

        Assert.Equal(SetupCheckLine.Unverified, line.State);
    }
}
