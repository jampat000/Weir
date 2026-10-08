using Weir.Core.MediaManagers;
using Weir.Core.Processing;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="LibraryFolderSuggestionRules"/>: which download client folder belongs to a media type, and the default output folder.</summary>
public sealed class LibraryFolderSuggestionRulesTests
{
    private static DownloadClientFolders Folders(string? completed, params (string Category, string Folder)[] categories) =>
        new(completed, [.. categories.Select(category => new DownloadClientCategoryFolder(category.Category, category.Folder))]);

    [Theory]
    [InlineData(@"C:\Downloads\Completed\Movies", "movie", @"C:\Downloads\Completed\Weir Ready\Movies")]
    [InlineData(@"C:\Downloads\Completed\TV", "tv", @"C:\Downloads\Completed\Weir Ready\TV")]
    [InlineData(@"C:\Downloads", "movie", @"C:\Weir Ready\Movies")]
    [InlineData("/data/downloads/complete", "tv", "/data/downloads/Weir Ready/TV")]
    [InlineData(@"\\nas\media\downloads", "movie", @"\\nas\media\Weir Ready\Movies")]
    public void The_default_output_folder_sits_beside_the_watched_folder(string watched, string mediaScope, string expected)
    {
        Assert.Equal(expected, LibraryFolderSuggestionRules.DefaultOutputFolder(watched, mediaScope));
    }

    [Theory]
    [InlineData("/downloads")]
    [InlineData(@"C:\")]
    [InlineData(@"\\nas\media")]
    [InlineData("downloads/complete")]
    [InlineData("")]
    public void There_is_no_default_output_folder_when_the_watched_folder_has_no_useful_parent(string watched)
    {
        Assert.Null(LibraryFolderSuggestionRules.DefaultOutputFolder(watched, "movie"));
    }

    [Fact]
    public void The_default_output_folder_never_overlaps_the_watched_folder()
    {
        var output = LibraryFolderSuggestionRules.DefaultOutputFolder("/data/downloads/movies", "movie")!;

        Assert.False(LibraryRules.FoldersOverlap(
            LibraryRules.NormalizeFolder("/data/downloads/movies")!,
            LibraryRules.NormalizeFolder(output)!));
    }

    [Theory]
    [InlineData("movie", "/downloads/movies")]
    [InlineData("tv", "/downloads/tv-sonarr")]
    public void A_category_named_for_the_media_type_gives_its_folder(string mediaScope, string expected)
    {
        var folders = Folders("/downloads", ("movies", "/downloads/movies"), ("tv-sonarr", "/downloads/tv-sonarr"));

        Assert.Equal(expected, LibraryFolderSuggestionRules.DownloadClientFolderFor(mediaScope, folders));
    }

    [Fact]
    public void A_category_named_for_the_application_counts_as_named_for_its_media_type()
    {
        var folders = Folders("/downloads", ("radarr", "/downloads/radarr"));

        Assert.Equal("/downloads/radarr", LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", folders));
        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("tv", folders));
    }

    [Fact]
    public void The_default_folder_every_media_type_saves_to_is_never_offered()
    {
        var folders = Folders("/downloads");

        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", folders));
        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("tv", folders));
    }

    [Fact]
    public void A_category_that_names_neither_media_type_gives_no_folder()
    {
        var folders = Folders("/downloads", ("software", "/downloads/software"));

        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", folders));
    }

    [Fact]
    public void A_category_that_names_both_media_types_is_taken_for_neither()
    {
        var folders = Folders("/downloads", ("tv-and-movies", "/downloads/both"));

        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("tv", folders));
        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", folders));
    }

    [Fact]
    public void A_client_with_no_folders_suggests_nothing()
    {
        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", DownloadClientFolders.Empty));
    }

    [Fact]
    public void A_relative_category_folder_is_not_offered()
    {
        var folders = Folders(null, ("movies", "movies"));

        Assert.Null(LibraryFolderSuggestionRules.DownloadClientFolderFor("movie", folders));
    }
}
