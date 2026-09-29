using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// <see cref="FinalPaths"/> and what leans on it, against real folders: an NTFS junction on Windows and a symbolic link
/// elsewhere stand in for a NAS mount that reaches one folder by two names.
/// </summary>
public sealed class FinalPathsTests
{
    [Fact]
    public void A_link_to_a_folder_resolves_to_the_folder_it_leads_to()
    {
        using var temp = new TempDirectory();
        var real = Directory.CreateDirectory(temp.Join("Downloads")).FullName;
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, real);

        Assert.Equal(real, FinalPaths.Resolve(mount), ignoreCase: true);
    }

    [Fact]
    public void A_folder_below_a_link_resolves_through_the_link()
    {
        using var temp = new TempDirectory();
        var real = Directory.CreateDirectory(temp.Join("Downloads", "Completed", "Movies")).FullName;
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, temp.Join("Downloads"));

        Assert.Equal(real, FinalPaths.Resolve(Path.Join(mount, "Completed", "Movies")), ignoreCase: true);
    }

    [Fact]
    public void A_chain_of_links_resolves_to_the_last_folder()
    {
        using var temp = new TempDirectory();
        var real = Directory.CreateDirectory(temp.Join("Downloads")).FullName;
        FolderLinks.Create(temp.Join("Second"), real);
        FolderLinks.Create(temp.Join("First"), temp.Join("Second"));

        Assert.Equal(real, FinalPaths.Resolve(temp.Join("First")), ignoreCase: true);
    }

    [Fact]
    public void A_plain_folder_resolves_to_itself()
    {
        using var temp = new TempDirectory();
        var plain = Directory.CreateDirectory(temp.Join("Downloads")).FullName;

        Assert.Equal(plain, FinalPaths.Resolve(plain), ignoreCase: true);
    }

    [Fact]
    public void A_folder_that_does_not_exist_here_does_not_resolve()
    {
        using var temp = new TempDirectory();

        Assert.Null(FinalPaths.Resolve(temp.Join("not-here")));
    }

    [Fact]
    public void A_link_whose_folder_has_gone_does_not_resolve()
    {
        using var temp = new TempDirectory();
        var real = Directory.CreateDirectory(temp.Join("Downloads")).FullName;
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, real);
        Directory.Delete(real);

        Assert.Null(FinalPaths.Resolve(mount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/folder")]
    public void A_path_that_is_not_fully_qualified_does_not_resolve(string path)
    {
        Assert.Null(FinalPaths.Resolve(path));
    }

    [Fact]
    public void The_probe_resolves_a_link_the_same_way()
    {
        using var temp = new TempDirectory();
        var real = Directory.CreateDirectory(temp.Join("Downloads")).FullName;
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, real);

        Assert.Equal(real, new FilesystemFolderProbe().ResolveFinalPath(mount), ignoreCase: true);
    }

    [Fact]
    public void A_hand_off_named_through_a_link_to_the_watched_folder_is_placed_in_that_library()
    {
        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Downloads", "Completed", "Movies")).FullName;
        File.WriteAllText(Path.Join(watched, "Film (2020).mkv"), "x");
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, temp.Join("Downloads"));
        IntakeLibrary[] libraries = [new(7, "movie", watched)];

        var chosen = HandoffThroughLinks.ChooseLibrary(libraries, HandOff(Path.Join(mount, "Completed", "Movies", "Film (2020).mkv")));

        Assert.NotNull(chosen);
        Assert.Equal(7, chosen.Value.Library.Id);
        Assert.Equal(watched, chosen.Value.Library.WatchedFolder);
        Assert.Equal("Film (2020).mkv", chosen.Value.Resolved.RelativeMediaPath);
    }

    [Fact]
    public void A_hand_off_through_a_link_to_a_folder_outside_every_watched_folder_is_not_placed()
    {
        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Downloads", "Completed", "Movies")).FullName;
        var elsewhere = Directory.CreateDirectory(temp.Join("Elsewhere")).FullName;
        File.WriteAllText(Path.Join(elsewhere, "Film (2020).mkv"), "x");
        var mount = temp.Join("NasMount");
        FolderLinks.Create(mount, elsewhere);
        IntakeLibrary[] libraries = [new(7, "movie", watched)];

        Assert.Null(HandoffThroughLinks.ChooseLibrary(libraries, HandOff(Path.Join(mount, "Film (2020).mkv"))));
    }

    [Fact]
    public void A_hand_off_for_a_file_that_is_not_here_is_not_placed()
    {
        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Downloads")).FullName;
        IntakeLibrary[] libraries = [new(7, "movie", watched)];

        Assert.Null(HandoffThroughLinks.ChooseLibrary(libraries, HandOff(Path.Join(watched, "missing.mkv"))));
    }

    private static MediaManagerImportEvent HandOff(string filePath) => new()
    {
        SourceKey = "deluno",
        EventKind = MediaManagerImportEvent.Handoff,
        MediaScope = "movie",
        FilePath = filePath,
    };
}
