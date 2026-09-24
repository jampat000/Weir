using Weir.Core.LibraryMode;

namespace Weir.Core.Tests.LibraryMode;

public sealed class OriginalsPathPlannerTests
{
    [Fact]
    public void The_folder_containing_a_file_is_found_among_several()
    {
        var folders = new[] { "/lib/movies", "/lib/shows" };
        Assert.Equal("/lib/shows", OriginalsPathPlanner.ContainingFolder(folders, "/lib/shows/Show/S01E01.mkv"));
    }

    [Fact]
    public void No_folder_containing_the_file_is_null()
    {
        Assert.Null(OriginalsPathPlanner.ContainingFolder(["/lib/movies"], "/elsewhere/film.mkv"));
    }

    [Theory]
    [InlineData("/lib", "/lib", true)]
    [InlineData("/lib", "/lib/film.mkv", true)]
    [InlineData("/lib/", "/lib/film.mkv", true)]
    [InlineData("/lib", "/library/film.mkv", false)]
    [InlineData("/lib", "/lib2/film.mkv", false)]
    public void IsWithin_matches_the_folder_itself_and_anything_inside_it_only(string folder, string path, bool expected)
    {
        Assert.Equal(expected, OriginalsPathPlanner.IsWithin(folder, path));
    }

    [Fact]
    public void The_default_destination_folder_is_dot_weir_originals_inside_the_library_folder()
    {
        Assert.Equal("/lib/movies/.weir-originals", OriginalsPathPlanner.DestinationFolder("/lib/movies", originalsFolder: null));
        Assert.Equal("/lib/movies/.weir-originals", OriginalsPathPlanner.DestinationFolder("/lib/movies", originalsFolder: "  "));
    }

    [Fact]
    public void The_default_destination_folder_keeps_a_windows_library_folders_own_separator()
    {
        Assert.Equal(
            @"D:\Media\Movies\.weir-originals",
            OriginalsPathPlanner.DestinationFolder(@"D:\Media\Movies", originalsFolder: null));
    }

    [Fact]
    public void An_explicit_originals_folder_is_used_as_is()
    {
        Assert.Equal("/backups/originals", OriginalsPathPlanner.DestinationFolder("/lib/movies", "/backups/originals"));
    }

    [Fact]
    public void The_destination_path_keeps_the_files_position_relative_to_its_library_folder()
    {
        var destination = OriginalsPathPlanner.DestinationPath("/lib/movies", null, "/lib/movies/Film (2020)/Film (2020).mkv");
        Assert.Equal("/lib/movies/.weir-originals/Film (2020)/Film (2020).mkv", destination);
    }

    [Fact]
    public void A_free_name_is_used_unchanged()
    {
        Assert.Equal("/originals/film.mkv", OriginalsPathPlanner.AvoidCollision("/originals/film.mkv", _ => false));
    }

    [Fact]
    public void A_clash_gets_a_numbered_suffix_and_keeps_looking_until_one_is_free()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "/originals/film.mkv", "/originals/film (2).mkv" };

        var result = OriginalsPathPlanner.AvoidCollision("/originals/film.mkv", taken.Contains);

        Assert.Equal("/originals/film (3).mkv", result);
    }

    [Fact]
    public void A_clash_on_a_windows_style_path_stays_windows_style()
    {
        var result = OriginalsPathPlanner.AvoidCollision(@"D:\Media\.weir-originals\film.mkv", path => path == @"D:\Media\.weir-originals\film.mkv");
        Assert.Equal(@"D:\Media\.weir-originals\film (2).mkv", result);
    }
}
