using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// <see cref="FinalPaths"/> against real folders: an NTFS junction on Windows and a symbolic link
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
}
