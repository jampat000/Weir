using Weir.Infrastructure.IO;

namespace Weir.Infrastructure.Tests.IO;

public sealed class PathContainmentTests
{
    private static readonly string Root = Path.Join(Path.GetTempPath(), "weir-containment", "movies");

    [Fact]
    public void A_sibling_that_shares_the_prefix_is_not_under_the_root()
    {
        Assert.False(PathContainment.IsUnder(Root, Root + "-old"));
        Assert.False(PathContainment.IsUnder(Root, Path.Join(Root + "-old", "Film.mkv")));
    }

    [Fact]
    public void A_nested_file_or_folder_is_under_the_root()
    {
        Assert.True(PathContainment.IsUnder(Root, Path.Join(Root, "Film.2024")));
        Assert.True(PathContainment.IsUnder(Root, Path.Join(Root, "Film.2024", "Film.2024.mkv")));
        Assert.True(PathContainment.IsUnder(Root + Path.DirectorySeparatorChar, Path.Join(Root, "Film.2024")));
    }

    [Fact]
    public void The_root_itself_is_not_under_the_root()
    {
        Assert.False(PathContainment.IsUnder(Root, Root));
        Assert.False(PathContainment.IsUnder(Root, Root + Path.DirectorySeparatorChar));
        Assert.False(PathContainment.IsUnder(Root, Path.Join(Root, "Film.2024", "..")));
    }

    [Fact]
    public void Dot_dot_segments_cannot_climb_out_of_the_root()
    {
        Assert.False(PathContainment.IsUnder(Root, Path.Join(Root, "..", "tv", "Show.mkv")));
        Assert.False(PathContainment.IsUnder(Root, Path.Join(Root, "Film.2024", "..", "..", "movies-old", "x.mkv")));
        Assert.True(PathContainment.IsUnder(Root, Path.Join(Root, "a", "..", "b", "Film.mkv")));
    }

    [Fact]
    public void A_parent_folder_is_not_under_its_child()
    {
        Assert.False(PathContainment.IsUnder(Path.Join(Root, "Film.2024"), Root));
    }

    [Fact]
    public void Case_is_ignored_on_windows_only()
    {
        var upper = Path.Join(Root.ToUpperInvariant(), "Film.mkv");
        Assert.Equal(OperatingSystem.IsWindows(), PathContainment.IsUnder(Root, upper));
    }

    [Fact]
    public void Everything_below_a_filesystem_root_is_under_it()
    {
        var driveRoot = Path.GetPathRoot(Root)!;
        Assert.True(PathContainment.IsUnder(driveRoot, Root));
        Assert.False(PathContainment.IsUnder(driveRoot, driveRoot));
    }
}
