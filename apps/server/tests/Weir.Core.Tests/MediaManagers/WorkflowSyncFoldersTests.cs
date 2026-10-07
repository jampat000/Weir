using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="WorkflowSyncFolders"/>: which folders a workflow set up from Deluno watches and writes to.</summary>
public sealed class WorkflowSyncFoldersTests
{
    private static DelunoDestination Client(string? saveFolder, string name = "qBittorrent") =>
        new(name, "radarr", "category", saveFolder, "client-category", DelunoDestination.Ok, string.Empty);

    private static DelunoLibraryDestinations Library(
        string?[] saveFolders, string? downloads = null, string? processed = null, (string Deluno, string Weir)[]? mappings = null) =>
        new(
            "lib-movies",
            "Movies",
            downloads,
            processed,
            [.. saveFolders.Select(folder => Client(folder))],
            [.. (mappings ?? []).Select(mapping => new DelunoPathMapping(mapping.Deluno, mapping.Weir))]);

    private static SyncedWatchedFolder Watched(string? downloadsPath, DelunoLibraryDestinations? destinations) =>
        WorkflowSyncFolders.Watched("Deluno", "Movies", downloadsPath, destinations);

    [Fact]
    public void Destinations_alone_give_the_folder_every_client_saves_to()
    {
        var result = Watched(null, Library([@"C:\Downloads\Completed\Movies"]));

        Assert.Equal(@"C:\Downloads\Completed\Movies", result.Folder);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void Clients_saving_to_the_same_folder_written_differently_give_that_one_folder()
    {
        var result = Watched(null, Library([@"C:\Downloads\Completed\Movies", "c:/downloads/completed/movies/"]));

        Assert.Equal(@"C:\Downloads\Completed\Movies", result.Folder);
    }

    [Fact]
    public void The_downloads_folder_alone_is_the_fallback_when_no_destinations_were_published()
    {
        var result = Watched(@"C:\Downloads\Completed\Movies", null);

        Assert.Equal(@"C:\Downloads\Completed\Movies", result.Folder);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void The_downloads_folder_is_used_when_the_clients_name_no_folder_and_goes_through_the_path_mappings()
    {
        var destinations = Library([null], mappings: [(@"C:\NasMount", @"C:\Downloads")]);

        var result = Watched(@"C:\NasMount\Completed\Movies", destinations);

        Assert.Equal(@"C:\Downloads\Completed\Movies", result.Folder);
    }

    [Fact]
    public void Destinations_win_when_they_disagree_with_the_downloads_folder()
    {
        var result = Watched(@"C:\Elsewhere\Movies", Library([@"C:\Downloads\Completed\Movies"], downloads: @"C:\Elsewhere\Movies"));

        Assert.Equal(@"C:\Downloads\Completed\Movies", result.Folder);
    }

    [Fact]
    public void Neither_a_client_folder_nor_a_downloads_folder_leaves_no_folder_and_says_what_to_set()
    {
        var result = Watched(null, Library([null]));

        Assert.Null(result.Folder);
        Assert.Equal(
            "Deluno doesn't say where downloads for Movies arrive. Set the downloads folder in Deluno (or the clients' category folders) and Weir will pick it up.",
            result.Problem);
    }

    [Fact]
    public void A_blank_downloads_folder_and_no_destinations_is_neither()
    {
        Assert.Null(Watched("  ", null).Folder);
        Assert.NotNull(Watched("  ", null).Problem);
    }

    [Fact]
    public void A_client_folder_in_Delunos_view_is_turned_into_Weirs_through_the_path_mappings()
    {
        var result = Watched(null, Library(["/data/completed/movies"], mappings: [("/data", @"D:\Media")]));

        Assert.Equal(@"D:\Media\completed\movies", result.Folder);
    }

    [Fact]
    public void Clients_saving_to_sibling_folders_give_the_folder_that_holds_them_both()
    {
        var result = Watched(null, Library([@"C:\Downloads\Completed\Movies", @"C:\Downloads\Completed\Movies 4K"]));

        Assert.Equal(@"C:\Downloads\Completed", result.Folder);
    }

    [Fact]
    public void The_common_parent_works_on_Unix_paths_too_and_compares_them_by_case()
    {
        var result = Watched(null, Library(["/data/Completed/movies", "/data/Completed/films", "/data/completed/other"]));

        Assert.Equal("/data", result.Folder);
    }

    [Fact]
    public void Clients_whose_only_common_parent_is_a_drive_root_give_the_first_folder()
    {
        var result = Watched(null, Library([@"C:\Movies", @"C:\Films"]));

        Assert.Equal(@"C:\Movies", result.Folder);
    }

    [Fact]
    public void Clients_whose_only_common_parent_is_the_filesystem_root_give_the_first_folder()
    {
        var result = Watched(null, Library(["/movies", "/films"]));

        Assert.Equal("/movies", result.Folder);
    }

    [Fact]
    public void Clients_on_different_drives_give_the_first_folder()
    {
        var result = Watched(null, Library([@"C:\Downloads\Movies", @"D:\Downloads\Movies"]));

        Assert.Equal(@"C:\Downloads\Movies", result.Folder);
    }

    [Fact]
    public void The_common_parent_of_network_shares_is_not_the_share_itself()
    {
        var result = Watched(null, Library([@"\\nas\media\Movies", @"\\nas\media\Films"]));

        Assert.Equal(@"\\nas\media\Movies", result.Folder);
    }

    [Fact]
    public void The_output_folder_goes_through_the_path_mappings()
    {
        var destinations = Library([null], mappings: [(@"C:\NasMount", @"C:\Weir")]);

        Assert.Equal(@"C:\Weir\Ready\Movies", WorkflowSyncFolders.Output(@"C:\NasMount\Ready\Movies", destinations));
    }

    [Fact]
    public void The_output_folder_is_used_as_Deluno_wrote_it_when_there_are_no_mappings()
    {
        Assert.Equal(@"C:\Weir\Ready\Movies", WorkflowSyncFolders.Output(@"C:\Weir\Ready\Movies", null));
    }

    [Fact]
    public void No_output_folder_is_none()
    {
        Assert.Null(WorkflowSyncFolders.Output(null, null));
        Assert.Null(WorkflowSyncFolders.Output(" ", null));
    }
}
