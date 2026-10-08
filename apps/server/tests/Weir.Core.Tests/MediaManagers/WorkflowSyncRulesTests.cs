using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="WorkflowSyncRules"/>: which workflow each Deluno library gets, and what changes.</summary>
public sealed class WorkflowSyncRulesTests
{
    private const long Deluno = 5;

    private static SyncedLibrary Movies(string? watched = @"C:\Downloads\Movies", string? output = @"C:\Weir\Ready\Movies", string key = "lib-movies", string name = "Movies") =>
        new(key, name, "movie", watched, output, null);

    private static SyncedLibrary Tv(string? watched = @"C:\Downloads\TV", string? output = @"C:\Weir\Ready\TV") =>
        new("lib-tv", "TV", "tv", watched, output, null);

    private static SyncWorkflow Workflow(
        long id,
        string mediaType = "movie",
        string watched = "",
        string output = "",
        long? from = null,
        string? key = null,
        bool synced = false,
        string? name = null,
        long[]? linkedTo = null) =>
        new(id, name ?? (mediaType == "tv" ? "TV" : "Movies"), mediaType, watched, output, from, key, synced, linkedTo ?? (from is { } connection ? [connection] : []));

    [Fact]
    public void With_no_workflow_at_all_a_library_gets_a_new_one()
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies()], []);

        var action = Assert.Single(plan);
        Assert.Equal(WorkflowSyncKind.Create, action.Kind);
        Assert.Null(action.Workflow);
        Assert.True(action.ChangesWatched);
        Assert.True(action.ChangesOutput);
    }

    [Fact]
    public void An_unconfigured_workflow_of_the_same_media_type_is_adopted_instead_of_a_duplicate_being_made()
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies(), Tv()], [Workflow(1), Workflow(2, "tv")]);

        Assert.Collection(
            plan,
            movies =>
            {
                Assert.Equal(WorkflowSyncKind.Adopt, movies.Kind);
                Assert.Equal(1, movies.Workflow!.Id);
            },
            tv =>
            {
                Assert.Equal(WorkflowSyncKind.Adopt, tv.Kind);
                Assert.Equal(2, tv.Workflow!.Id);
            });
    }

    [Fact]
    public void A_workflow_someone_linked_to_the_manager_by_hand_is_taken_over_instead_of_a_second_one_being_made()
    {
        var handLinked = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", linkedTo: [Deluno]);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [handLinked]));

        Assert.Equal(WorkflowSyncKind.Adopt, action.Kind);
        Assert.Equal(1, action.Workflow!.Id);
    }

    [Fact]
    public void A_workflow_linked_by_hand_is_preferred_to_an_unconfigured_one()
    {
        var unconfigured = Workflow(1);
        var handLinked = Workflow(2, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", linkedTo: [Deluno]);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [unconfigured, handLinked]));

        Assert.Equal((WorkflowSyncKind.Adopt, 2L), (action.Kind, action.Workflow!.Id));
    }

    [Fact]
    public void A_workflow_linked_by_hand_to_another_manager_of_another_media_type_or_with_other_folders_is_not_taken_over()
    {
        var otherManager = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", linkedTo: [Deluno + 1]);
        var otherType = Workflow(2, "tv", watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", linkedTo: [Deluno]);
        var otherFolders = Workflow(3, watched: @"C:\Somewhere\Else", output: @"C:\Weir\Ready\Movies", linkedTo: [Deluno]);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [otherManager, otherType, otherFolders]));

        Assert.Equal(WorkflowSyncKind.Create, action.Kind);
    }

    [Fact]
    public void A_workflow_of_the_other_media_type_is_not_adopted()
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies()], [Workflow(2, "tv")]);

        Assert.Equal(WorkflowSyncKind.Create, Assert.Single(plan).Kind);
    }

    [Theory]
    [InlineData(@"C:\Mine\Movies", "")]
    [InlineData("", @"C:\Mine\Ready")]
    public void A_workflow_with_a_folder_of_its_own_is_not_adopted(string watched, string output)
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies()], [Workflow(1, watched: watched, output: output)]);

        Assert.Equal(WorkflowSyncKind.Create, Assert.Single(plan).Kind);
    }

    [Fact]
    public void A_workflow_linked_to_another_manager_library_is_not_adopted()
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies()], [Workflow(1, from: 9, key: "other")]);

        Assert.Equal(WorkflowSyncKind.Create, Assert.Single(plan).Kind);
    }

    [Fact]
    public void Two_libraries_of_one_media_type_adopt_one_workflow_and_create_the_other()
    {
        var plan = WorkflowSyncRules.Plan(Deluno, [Movies(), Movies(key: "lib-4k", name: "Movies 4K")], [Workflow(1)]);

        Assert.Equal([WorkflowSyncKind.Adopt, WorkflowSyncKind.Create], plan.Select(action => action.Kind));
    }

    [Fact]
    public void The_workflow_already_linked_to_a_library_is_updated_not_replaced()
    {
        var linked = Workflow(1, watched: @"C:\Old\Movies", output: @"C:\Old\Ready", from: Deluno, key: "lib-movies", synced: true);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [linked, Workflow(2)]));

        Assert.Equal(WorkflowSyncKind.Update, action.Kind);
        Assert.Equal(1, action.Workflow!.Id);
        Assert.True(action.ChangesWatched);
        Assert.True(action.ChangesOutput);
    }

    [Fact]
    public void Only_the_folder_that_changed_is_rewritten()
    {
        var linked = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Old\Ready", from: Deluno, key: "lib-movies", synced: true);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [linked]));

        Assert.False(action.ChangesWatched);
        Assert.True(action.ChangesOutput);
    }

    [Fact]
    public void A_folder_written_a_different_way_round_is_not_a_change()
    {
        var linked = Workflow(1, watched: "c:/downloads/movies/", output: @"C:\WEIR\READY\MOVIES", from: Deluno, key: "lib-movies", synced: true);

        Assert.Empty(WorkflowSyncRules.Plan(Deluno, [Movies()], [linked]));
    }

    [Fact]
    public void A_folder_the_manager_does_not_say_is_never_written_over_a_folder_the_workflow_has()
    {
        var linked = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-movies", synced: true);

        Assert.Empty(WorkflowSyncRules.Plan(Deluno, [Movies(watched: null)], [linked]));
    }

    [Fact]
    public void A_watched_folder_the_manager_gives_no_folder_for_is_cleared_when_it_is_the_same_as_another_workflows()
    {
        var movies = Workflow(1, watched: @"C:\Downloads", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-movies", synced: true);
        var tv = Workflow(2, "tv", watched: @"c:\downloads\", output: @"C:\Weir\Ready\TV", from: Deluno, key: "lib-tv", synced: true);

        var plan = WorkflowSyncRules.Plan(Deluno, [Movies(watched: null), Tv(watched: null)], [movies, tv]);

        Assert.All(plan, action =>
        {
            Assert.Equal(WorkflowSyncKind.Update, action.Kind);
            Assert.True(action.ClearsWatched);
            Assert.False(action.ChangesWatched);
            Assert.False(action.ChangesOutput);
        });
        Assert.Equal([1L, 2L], plan.Select(action => action.Workflow!.Id));
    }

    [Fact]
    public void A_watched_folder_that_holds_another_workflows_is_cleared_and_the_folder_inside_it_is_kept()
    {
        var movies = Workflow(1, watched: @"C:\Downloads", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-movies", synced: true);
        var tv = Workflow(2, "tv", watched: @"C:\Downloads\TV", output: @"C:\Weir\Ready\TV", from: Deluno, key: "lib-tv", synced: true);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies(watched: null), Tv(watched: null)], [movies, tv]));

        Assert.Equal(1, action.Workflow!.Id);
        Assert.True(action.ClearsWatched);
    }

    [Fact]
    public void A_watched_folder_nothing_else_overlaps_is_kept_when_the_manager_gives_no_folder()
    {
        var movies = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-movies", synced: true);
        var mine = Workflow(2, "tv", watched: @"C:\Mine\TV", output: @"C:\Mine\Out");

        Assert.Empty(WorkflowSyncRules.Plan(Deluno, [Movies(watched: null)], [movies, mine]));
    }

    [Fact]
    public void A_watched_folder_is_not_cleared_when_the_manager_gives_the_library_a_folder()
    {
        var movies = Workflow(1, watched: @"C:\Downloads", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-movies", synced: true);
        var tv = Workflow(2, "tv", watched: @"C:\Downloads\TV", output: @"C:\Weir\Ready\TV", from: Deluno, key: "lib-tv", synced: true);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies(), Tv()], [movies, tv]), planned => planned.Workflow!.Id == 1);

        Assert.False(action.ClearsWatched);
        Assert.True(action.ChangesWatched);
    }

    [Fact]
    public void A_workflow_that_was_unlinked_is_left_alone_and_no_second_one_is_made()
    {
        var unlinked = Workflow(1, watched: @"C:\Mine", output: @"C:\Mine\Out", from: Deluno, key: "lib-movies", synced: false);

        Assert.Empty(WorkflowSyncRules.Plan(Deluno, [Movies()], [unlinked, Workflow(2)]));
    }

    [Fact]
    public void A_workflow_whose_library_is_gone_or_no_longer_processes_with_Weir_is_not_touched_and_nothing_is_deleted()
    {
        var orphan = Workflow(1, watched: @"C:\Downloads\Movies", output: @"C:\Weir\Ready\Movies", from: Deluno, key: "lib-gone", synced: true);

        Assert.Empty(WorkflowSyncRules.Plan(Deluno, [], [orphan]));
    }

    [Fact]
    public void Workflows_linked_to_another_manager_are_not_updated()
    {
        var other = Workflow(1, watched: @"C:\Old", output: @"C:\Old\Out", from: 9, key: "lib-movies", synced: true);

        var action = Assert.Single(WorkflowSyncRules.Plan(Deluno, [Movies()], [other]));

        Assert.Equal(WorkflowSyncKind.Create, action.Kind);
    }

    [Fact]
    public void A_workflow_is_synced_while_it_is_linked_to_the_Deluno_it_came_from()
    {
        Assert.Equal(5, WorkflowSyncRules.FoldersSyncedFrom(5, "lib", [5], "deluno"));
        Assert.Equal(5, WorkflowSyncRules.FoldersSyncedFrom(5, "lib", [3, 5], "Deluno"));
    }

    [Fact]
    public void Unlinking_a_workflow_ends_the_sync()
    {
        Assert.Null(WorkflowSyncRules.FoldersSyncedFrom(5, "lib", [], "deluno"));
        Assert.Null(WorkflowSyncRules.FoldersSyncedFrom(null, null, [5], "deluno"));
        Assert.Null(WorkflowSyncRules.FoldersSyncedFrom(5, null, [5], "deluno"));
    }

    [Theory]
    [InlineData("sonarr")]
    [InlineData("radarr")]
    [InlineData("native")]
    [InlineData(null)]
    public void Managers_that_cannot_report_folders_never_own_a_workflows_folders(string? kind)
    {
        Assert.Null(WorkflowSyncRules.FoldersSyncedFrom(5, "lib", [5], kind));
        Assert.False(WorkflowSyncRules.ReportsFolders(kind));
    }

    private static ManagerLibraryDescriptor Descriptor(string key, string name, string scope, bool refining, string? downloads = null, string? output = null) =>
        new(key, name, scope, @"C:\Media", refining ? output : null, refining, downloads);

    [Fact]
    public void A_library_that_does_not_process_with_Weir_gets_no_workflow()
    {
        var libraries = WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [
                Descriptor("lib-movies", "Movies", "movie", refining: true, downloads: @"C:\Downloads\Movies", output: @"C:\Ready\Movies"),
                Descriptor("lib-direct", "Direct", "movie", refining: false, downloads: @"C:\Downloads\Direct"),
            ],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered));

        var only = Assert.Single(libraries);
        Assert.Equal("lib-movies", only.Key);
        Assert.Equal(@"C:\Downloads\Movies", only.WatchedFolder);
        Assert.Equal(@"C:\Ready\Movies", only.OutputFolder);
    }

    [Fact]
    public void Published_destinations_and_mappings_reach_the_library_that_owns_them()
    {
        var published = new DelunoLibraryDestinations(
            "LIB-MOVIES", "Movies", null, @"C:\Nas\Ready\Movies",
            [new DelunoDestination("qBittorrent", "radarr", "category", @"C:\Nas\Completed\Movies", "client-category", DelunoDestination.Ok, string.Empty)],
            [new DelunoPathMapping(@"C:\Nas", @"D:\Mount")]);

        var libraries = WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [Descriptor("lib-movies", "Movies", "movie", refining: true, output: @"C:\Nas\Ready\Movies")],
            DelunoDestinationsAnswer.Read([published]));

        var library = Assert.Single(libraries);
        Assert.Equal(@"D:\Mount\Completed\Movies", library.WatchedFolder);
        Assert.Equal(@"D:\Mount\Ready\Movies", library.OutputFolder);
        Assert.Null(library.Problem);
    }

    [Fact]
    public void A_library_with_no_downloads_folder_keeps_its_output_and_says_what_to_set()
    {
        var library = Assert.Single(WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [Descriptor("lib-movies", "Movies", "movie", refining: true, output: @"C:\Ready\Movies")],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered)));

        Assert.Null(library.WatchedFolder);
        Assert.Equal(@"C:\Ready\Movies", library.OutputFolder);
        Assert.Equal(WorkflowSyncFolders.NoDownloadsFolder("Deluno", "Movies"), library.Problem);
    }

    [Fact]
    public void Libraries_given_the_same_downloads_folder_get_none_and_are_told_to_give_each_its_own()
    {
        var libraries = WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [
                Descriptor("lib-movies", "Movies", "movie", refining: true, downloads: @"C:\Downloads\Completed", output: @"C:\Ready\Movies"),
                Descriptor("lib-tv", "TV", "tv", refining: true, downloads: @"c:/downloads/completed/", output: @"C:\Ready\TV"),
            ],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered));

        Assert.All(libraries, library => Assert.Null(library.WatchedFolder));
        Assert.Equal([@"C:\Ready\Movies", @"C:\Ready\TV"], libraries.Select(library => library.OutputFolder));
        Assert.Equal(WorkflowSyncFolders.SharedDownloadsFolder("Deluno", "Movies", @"C:\Downloads\Completed"), libraries[0].Problem);
        Assert.Equal(WorkflowSyncFolders.SharedDownloadsFolder("Deluno", "TV", @"c:/downloads/completed/"), libraries[1].Problem);
    }

    [Fact]
    public void A_downloads_folder_that_holds_another_librarys_is_not_given_but_the_folder_inside_it_is()
    {
        var libraries = WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [
                Descriptor("lib-movies", "Movies", "movie", refining: true, downloads: @"C:\Downloads\Completed", output: @"C:\Ready\Movies"),
                Descriptor("lib-tv", "TV", "tv", refining: true, downloads: @"C:\Downloads\Completed\TV", output: @"C:\Ready\TV"),
            ],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered));

        Assert.Null(libraries[0].WatchedFolder);
        Assert.NotNull(libraries[0].Problem);
        Assert.Equal(@"C:\Downloads\Completed\TV", libraries[1].WatchedFolder);
        Assert.Null(libraries[1].Problem);
    }

    [Fact]
    public void Folders_that_only_share_a_name_prefix_are_each_the_librarys_own()
    {
        var libraries = WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [
                Descriptor("lib-movies", "Movies", "movie", refining: true, downloads: @"C:\Downloads\Movies", output: @"C:\Ready\Movies"),
                Descriptor("lib-4k", "Movies 4K", "movie", refining: true, downloads: @"C:\Downloads\Movies 4K", output: @"C:\Ready\Movies 4K"),
            ],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered));

        Assert.Equal([@"C:\Downloads\Movies", @"C:\Downloads\Movies 4K"], libraries.Select(library => library.WatchedFolder));
    }

    [Fact]
    public void A_library_with_no_output_folder_gets_no_folders_because_a_watched_folder_needs_an_output_folder()
    {
        var library = Assert.Single(WorkflowSyncRules.LibrariesOf(
            "Deluno",
            [Descriptor("lib-movies", "Movies", "movie", refining: true, downloads: @"C:\Downloads\Movies")],
            DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.Unreachable)));

        Assert.Null(library.WatchedFolder);
        Assert.Null(library.OutputFolder);
        Assert.Equal(
            "Deluno doesn't say where it picks up processed files for Movies. Set the processed folder for that library in Deluno and Weir will pick it up.",
            library.Problem);
    }

    private static ManagerLibraryDescriptor Listed(string key, bool refines) => new(key, key, "movies", ProcessesBeforeImport: refines);

    [Fact]
    public void A_synced_workflow_whose_library_is_gone_or_no_longer_refines_has_departed()
    {
        var gone = Workflow(1, from: Deluno, key: "lib-gone", synced: true, name: "Gone");
        var stopped = Workflow(2, from: Deluno, key: "lib-stopped", synced: true, name: "Stopped");
        var fine = Workflow(3, from: Deluno, key: "lib-fine", synced: true, name: "Fine");

        var departed = WorkflowSyncRules.Departed(Deluno, [Listed("lib-stopped", false), Listed("lib-fine", true)], [gone, stopped, fine]);

        Assert.Collection(
            departed,
            first =>
            {
                Assert.Equal(1, first.Workflow.Id);
                Assert.Null(first.Library);
            },
            second =>
            {
                Assert.Equal(2, second.Workflow.Id);
                Assert.Equal("lib-stopped", second.Library!.Key);
            });
    }

    [Fact]
    public void An_unlinked_workflow_or_one_from_another_manager_or_one_never_set_up_has_not_departed()
    {
        var unlinked = Workflow(1, from: Deluno, key: "lib-a", synced: false);
        var other = Workflow(2, from: Deluno + 1, key: "lib-b", synced: true);
        var mine = Workflow(3);

        Assert.Empty(WorkflowSyncRules.Departed(Deluno, [], [unlinked, other, mine]));
    }

    [Fact]
    public void A_departure_says_the_workflow_was_left_as_it_is_and_what_to_do()
    {
        var workflow = Workflow(1, from: Deluno, key: "lib-movies", synced: true);
        var gone = new DepartedWorkflow(workflow, null);
        var stopped = new DepartedWorkflow(workflow, new ManagerLibraryDescriptor("lib-movies", "Movies", "movies"));

        Assert.Equal("Deluno no longer has the library for Movies", WorkflowSyncRules.DepartedTitle(gone, "Deluno"));
        Assert.Equal("Deluno no longer has the library this workflow came from. Weir left Movies as it is.", WorkflowSyncRules.DepartedMessage(gone, "Deluno"));
        Assert.Equal(
            "Remove this workflow if the library is gone for good, or unlink it from Deluno to keep it as a Weir-only workflow.",
            WorkflowSyncRules.DepartedNextAction(gone, "Deluno"));
        Assert.Equal("Deluno no longer hands Movies to Weir", WorkflowSyncRules.DepartedTitle(stopped, "Deluno"));
        Assert.Equal(
            "Deluno's Movies library is no longer set to Refine before import, so Deluno will not hand movie downloads to Weir. Weir left Movies as it is.",
            WorkflowSyncRules.DepartedMessage(stopped, "Deluno"));
        Assert.Equal(
            "Choose Refine before import for that library in Deluno. If you no longer want Movies, unlink it from Deluno or remove it in Weir.",
            WorkflowSyncRules.DepartedNextAction(stopped, "Deluno"));
    }

    [Fact]
    public void The_events_are_worded_for_the_person()
    {
        Assert.Equal("Workflow Movies set up from Deluno", WorkflowSyncRules.SetUpTitle("Movies", "Deluno"));
        Assert.Equal("Movies' watched folder updated from Deluno", WorkflowSyncRules.UpdatedTitle("Movies", "Deluno", true, false));
        Assert.Equal("TV's watched folder updated from Deluno", WorkflowSyncRules.UpdatedTitle("TV", "Deluno", true, false));
        Assert.Equal("Movies' output folder updated from Deluno", WorkflowSyncRules.UpdatedTitle("Movies", "Deluno", false, true));
        Assert.Equal("Movies' watched and output folders updated from Deluno", WorkflowSyncRules.UpdatedTitle("Movies", "Deluno", true, true));
        Assert.Equal(
            @"Deluno now reports C:\New for downloads for Movies, so Weir changed it to match.",
            WorkflowSyncRules.UpdatedMessage("Movies", "Deluno", @"C:\New", null));
    }
}
