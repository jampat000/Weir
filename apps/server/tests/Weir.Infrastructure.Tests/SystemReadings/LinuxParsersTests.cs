using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

/// <summary>Reading the files /proc and /sys serve, from text the tests supply.</summary>
public sealed class LinuxParsersTests
{
    [Fact]
    public void Idle_time_includes_waiting_for_disk_and_the_total_is_the_first_eight_columns()
    {
        var times = LinuxParsers.ParseCpuTimes("cpu  100 20 30 400 50 6 7 8 900 1000\ncpu0 1 1 1 1 1 1 1 1 1 1\n");

        Assert.Equal(new CpuTimes(Idle: 450, Total: 621), times);
    }

    [Fact]
    public void A_stat_file_without_a_cpu_line_gives_no_times()
    {
        Assert.Null(LinuxParsers.ParseCpuTimes("intr 12345\n"));
        Assert.Null(LinuxParsers.ParseCpuTimes(null));
    }

    [Fact]
    public void Memory_is_total_and_available_in_bytes()
    {
        var memory = LinuxParsers.ParseMemory("MemTotal:       16384 kB\nMemFree:  100 kB\nMemAvailable:    4096 kB\n");

        Assert.Equal(new MemoryReading(16384L * 1024, 4096L * 1024), memory);
        Assert.Equal(12288L * 1024, memory!.Value.Used);
    }

    [Fact]
    public void Memory_without_an_available_figure_is_unknown()
    {
        Assert.Null(LinuxParsers.ParseMemory("MemTotal: 16384 kB\n"));
    }

    [Fact]
    public void Uptime_is_the_first_number_in_whole_seconds()
    {
        Assert.Equal(12345, LinuxParsers.ParseUptimeSeconds("12345.67 98765.43\n"));
        Assert.Null(LinuxParsers.ParseUptimeSeconds("nonsense"));
    }

    [Fact]
    public void The_operating_system_is_its_pretty_name()
    {
        Assert.Equal("Debian GNU/Linux 12 (bookworm)", LinuxParsers.ParseOsPrettyName("NAME=\"Debian\"\nPRETTY_NAME=\"Debian GNU/Linux 12 (bookworm)\"\n"));
        Assert.Null(LinuxParsers.ParseOsPrettyName("NAME=Debian\n"));
    }

    [Fact]
    public void Disk_counters_convert_sectors_to_bytes_and_idle_is_uptime_less_the_time_spent_on_io()
    {
        const string diskstats = "   8       0 sda 100 0 2000 50 50 0 4000 70 0 600 0\n";

        var stats = LinuxParsers.ParseDiskStats(diskstats, uptimeMilliseconds: 10_000);

        var (name, counters) = stats["8:0"];
        Assert.Equal("sda", name);
        Assert.Equal(new DiskCounters(BytesRead: 2000 * 512, BytesWritten: 4000 * 512, IdleTime: 9_400, QueryTime: 10_000), counters);
    }

    [Fact]
    public void Short_disk_lines_are_ignored()
    {
        Assert.Empty(LinuxParsers.ParseDiskStats("8 0 sda 1 2\n", 10_000));
    }

    [Fact]
    public void A_path_belongs_to_the_mount_with_the_longest_matching_mount_point()
    {
        const string mountInfo =
            "22 1 8:1 / / rw - ext4 /dev/sda1 rw\n" +
            "30 22 8:16 / /mnt/media rw - ext4 /dev/sdb rw\n" +
            "31 22 0:50 / /mnt/my\\040drive rw - cifs //nas/x rw\n";

        Assert.Equal(new Mount("/mnt/media", "8:16"), LinuxParsers.MountOf(mountInfo, "/mnt/media/movies/a.mkv"));
        Assert.Equal(new Mount("/", "8:1"), LinuxParsers.MountOf(mountInfo, "/mnt/mediastuff"));
        Assert.Equal(new Mount("/mnt/my drive", "0:50"), LinuxParsers.MountOf(mountInfo, "/mnt/my drive/tv"));
    }

    [Theory]
    [InlineData("150000 100000", 1.5)]
    [InlineData("200000 100000\n", 2.0)]
    public void A_cpu_quota_is_a_number_of_cores(string cpuMax, double cores)
    {
        Assert.Equal(cores, LinuxParsers.ParseCpuMaxCores(cpuMax));
    }

    [Theory]
    [InlineData("max 100000")]
    [InlineData("")]
    [InlineData("garbage")]
    public void No_cpu_quota_means_no_limit(string cpuMax)
    {
        Assert.Null(LinuxParsers.ParseCpuMaxCores(cpuMax));
    }

    [Fact]
    public void Byte_limits_read_numbers_and_treat_max_as_unlimited()
    {
        Assert.Equal(536870912, LinuxParsers.ParseBytes("536870912\n"));
        Assert.Null(LinuxParsers.ParseBytes("max\n"));
        Assert.Null(LinuxParsers.ParseBytes(null));
    }

    [Fact]
    public void Cgroup_statistics_are_found_by_name()
    {
        Assert.Equal(5_000_000, LinuxParsers.ParseCpuUsageMicroseconds("usage_usec 5000000\nuser_usec 4000000\n"));
        Assert.Equal(777, LinuxParsers.ParseInactiveFileBytes("anon 1\ninactive_file 777\n"));
    }
}
