using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

public sealed class DriveReaderTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeHostReadingSource _host = new();

    public DriveReaderTests()
    {
        _host.RootsByFolder["D:\\media"] = "D:\\";
        _host.RootsByFolder["D:\\work"] = "D:\\";
        _host.RootsByFolder["\\\\nas\\tv"] = "\\\\nas\\tv\\";
        _host.SpaceByRoot["D:\\"] = new DriveSpace(2000 * Gigabyte, 1500 * Gigabyte);
        _host.SpaceByRoot["\\\\nas\\tv\\"] = new DriveSpace(8000 * Gigabyte, 3000 * Gigabyte);
        _host.VolumeKeysByRoot["D:\\"] = "D:";
    }

    private DriveReader Reader(Func<string, long>? workFileBytes = null) =>
        new(_host, new FreeSpaceForecast(_time), _time, workFileBytes ?? (_ => 0));

    private static WorkflowFolder Folder(long id, string name, string role, string path, long keepFree = 0) => new(id, name, role, path, keepFree);

    [Fact]
    public void Each_drive_that_holds_a_workflow_folder_is_one_entry_with_its_size_and_free_space()
    {
        var folders = new[]
        {
            Folder(1, "Movies", WorkflowFolders.Watched, "D:\\media\\in"),
            Folder(1, "Movies", WorkflowFolders.Output, "D:\\media\\out"),
            Folder(2, "TV", WorkflowFolders.Output, "\\\\nas\\tv\\shows"),
        };

        var drives = Reader().Read(folders);

        Assert.Equal(["D:", "\\\\nas\\tv"], drives.Select(drive => drive.Name));
        Assert.Equal("D:\\", drives[0].Path);
        Assert.Equal((2000 * Gigabyte, 1500 * Gigabyte), (drives[0].TotalBytes, drives[0].FreeBytes));
    }

    [Fact]
    public void A_drive_lists_the_workflows_on_it_with_the_parts_their_folders_play_in_a_fixed_order()
    {
        var folders = new[]
        {
            Folder(2, "TV", WorkflowFolders.Output, "D:\\media\\tv"),
            Folder(1, "Movies", WorkflowFolders.Output, "D:\\media\\out"),
            Folder(1, "Movies", WorkflowFolders.Watched, "D:\\media\\in"),
            Folder(1, "Movies", WorkflowFolders.Work, "D:\\work"),
        };

        var workflows = Reader().Read(folders).Single().Workflows;

        Assert.Equal(["Movies", "TV"], workflows.Select(workflow => workflow.Name));
        Assert.Equal([WorkflowFolders.Watched, WorkflowFolders.Work, WorkflowFolders.Output], workflows[0].Roles);
        Assert.Equal([WorkflowFolders.Output], workflows[1].Roles);
    }

    [Fact]
    public void The_drive_keeps_free_the_most_any_workflow_on_it_wants()
    {
        var folders = new[]
        {
            Folder(1, "Movies", WorkflowFolders.Output, "D:\\media\\out", keepFree: 5 * Gigabyte),
            Folder(2, "TV", WorkflowFolders.Output, "D:\\media\\tv", keepFree: 20 * Gigabyte),
        };

        Assert.Equal(20 * Gigabyte, Reader().Read(folders).Single().KeepFreeBytes);
    }

    [Fact]
    public void Weirs_own_share_is_its_work_files_in_each_distinct_work_folder_on_the_drive()
    {
        var folders = new[]
        {
            Folder(1, "Movies", WorkflowFolders.Work, "D:\\work"),
            Folder(2, "TV", WorkflowFolders.Work, "D:\\work"),
            Folder(2, "TV", WorkflowFolders.Work, "D:\\work\\tv"),
            Folder(1, "Movies", WorkflowFolders.Output, "D:\\media\\out"),
        };
        var sizes = new Dictionary<string, long> { ["D:\\work"] = 3 * Gigabyte, ["D:\\work\\tv"] = 1 * Gigabyte };

        var drive = Reader(path => sizes[path]).Read(folders).Single();

        Assert.Equal(4 * Gigabyte, drive.WeirBytes);
    }

    [Fact]
    public void Disk_activity_comes_from_the_second_reading_for_a_local_drive_and_never_for_a_share()
    {
        var folders = new[]
        {
            Folder(1, "Movies", WorkflowFolders.Output, "D:\\media\\out"),
            Folder(2, "TV", WorkflowFolders.Output, "\\\\nas\\tv\\shows"),
        };
        var reader = Reader();
        _host.Disks = DiskSnapshot.FromVolumes(new Dictionary<string, DiskCounters> { ["D:"] = new(0, 0, 0, 0) });
        var first = reader.Read(folders);

        _time.Advance(TimeSpan.FromSeconds(30));
        _host.Disks = DiskSnapshot.FromVolumes(new Dictionary<string, DiskCounters> { ["D:"] = new(300_000_000, 60_000_000, 10_000, 30_000) });
        var second = reader.Read(folders);

        Assert.Null(first[0].ReadBytesPerSecond);
        Assert.Equal(10_000_000, second[0].ReadBytesPerSecond);
        Assert.Equal(2_000_000, second[0].WriteBytesPerSecond);
        Assert.Equal(66.7, second[0].BusyPercent);
        Assert.Null(second[1].ReadBytesPerSecond);
        Assert.Null(second[1].WriteBytesPerSecond);
        Assert.Null(second[1].BusyPercent);
    }

    [Fact]
    public void A_drive_whose_size_cannot_be_read_is_left_out()
    {
        _host.SpaceByRoot.Remove("\\\\nas\\tv\\");
        var folders = new[] { Folder(2, "TV", WorkflowFolders.Output, "\\\\nas\\tv\\shows") };

        Assert.Empty(Reader().Read(folders));
    }

    [Fact]
    public void A_folder_that_belongs_to_no_drive_is_left_out()
    {
        var folders = new[] { Folder(1, "Movies", WorkflowFolders.Output, "Z:\\nowhere") };

        Assert.Empty(Reader().Read(folders));
    }
}
