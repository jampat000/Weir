using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// <see cref="FilesystemFolderProbe"/> against real folders on disk (#768) — the folder chain check's only
/// filesystem-touching implementation of <see cref="Core.MediaManagers.IFolderProbe"/>.
/// </summary>
public sealed class FilesystemFolderProbeTests
{
    private readonly FilesystemFolderProbe _probe = new();

    [Fact]
    public void A_real_folder_exists_and_a_missing_one_does_not()
    {
        using var temp = new TempDirectory();

        Assert.True(_probe.Exists(temp.Path));
        Assert.False(_probe.Exists(temp.Join("does-not-exist")));
    }

    [Fact]
    public void A_readable_folder_can_be_read()
    {
        using var temp = new TempDirectory();

        Assert.True(_probe.CanRead(temp.Path));
    }

    [Fact]
    public void A_writable_folder_can_be_written_to_and_the_probe_file_is_cleaned_up()
    {
        using var temp = new TempDirectory();

        var ok = _probe.CanWrite(temp.Path);

        Assert.True(ok);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public void Two_folders_under_the_same_root_share_a_filesystem()
    {
        using var temp = new TempDirectory();
        var first = temp.Join("first");
        var second = temp.Join("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        Assert.True(_probe.SameFilesystem(first, second));
    }
}
