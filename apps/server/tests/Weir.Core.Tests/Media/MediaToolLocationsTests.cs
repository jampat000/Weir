using Weir.Core.Media;

namespace Weir.Core.Tests.Media;

/// <summary>
/// The order <see cref="MediaToolLocations"/> checks for ffmpeg/ffprobe and mkvmerge, and the PATH search's
/// rules (#723): an explicit override wins, then a packaged app's own bundle, then PATH — Weir's data folder
/// (<c>WEIR_HOME</c>) is never one of the candidates, because a default Windows install lets every local
/// account write to it.
/// </summary>
public sealed class MediaToolLocationsTests
{
    [Fact]
    public void The_ffmpeg_dir_override_comes_before_the_packaged_apps_own_bundle()
    {
        var directories = MediaToolLocations.CandidateDirectories(
            ffmpegDirEnvironment: @"C:\override",
            userHome: @"C:\Users\operator",
            packagedAppDirectory: @"C:\app",
            windows: true);

        Assert.Equal(
            [@"C:\override", @"C:\app\bin\ffmpeg", @"C:\app\_internal\bin\ffmpeg"],
            directories);
    }

    [Fact]
    public void With_no_override_and_no_packaged_app_there_are_no_candidate_directories_at_all()
    {
        // Nothing bundled and nothing configured means the only remaining source is PATH.
        var directories = MediaToolLocations.CandidateDirectories(
            ffmpegDirEnvironment: null,
            userHome: @"C:\Users\operator",
            packagedAppDirectory: null,
            windows: true);

        Assert.Empty(directories);
    }

    [Fact]
    public void The_weir_home_bin_folder_is_never_a_candidate_directory()
    {
        // A folder named "home" standing in for WEIR_HOME must never appear among the candidates, however it
        // is spelled: this is the whole point of the fix, so assert the negative directly.
        var directories = MediaToolLocations.CandidateDirectories(
            ffmpegDirEnvironment: null,
            userHome: @"C:\Users\operator",
            packagedAppDirectory: @"C:\app",
            windows: true);

        Assert.DoesNotContain(directories, d => d.Contains("home", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_mkvtoolnix_dir_override_comes_before_the_packaged_apps_own_bundle()
    {
        var directories = MediaToolLocations.MkvtoolnixCandidateDirectories(
            mkvtoolnixDirEnvironment: @"C:\override",
            userHome: @"C:\Users\operator",
            packagedAppDirectory: @"C:\app",
            windows: true);

        Assert.Equal(
            [@"C:\override", @"C:\app\bin\mkvtoolnix", @"C:\app\_internal\bin\mkvtoolnix"],
            directories);
    }

    [Fact]
    public void Which_only_accepts_an_exe_on_windows()
    {
        var seen = new List<string>();
        bool Exists(string candidate)
        {
            seen.Add(candidate);
            return candidate == @"C:\bin\ffprobe.exe";
        }

        var found = MediaToolLocations.Which("ffprobe", @"C:\bin", windows: true, Exists);

        Assert.Equal(@"C:\bin\ffprobe.exe", found);
        Assert.DoesNotContain(seen, c => c.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || c.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_bat_file_on_path_is_never_matched_even_when_it_is_the_only_candidate()
    {
        // Only ffprobe.bat exists; Which must come back empty rather than matching it.
        var found = MediaToolLocations.Which("ffprobe", @"C:\bin", windows: true, candidate => candidate == @"C:\bin\ffprobe.bat");

        Assert.Null(found);
    }

    [Fact]
    public void Which_never_searches_the_current_directory_on_windows()
    {
        var found = MediaToolLocations.Which("ffprobe", @"C:\bin", windows: true, candidate => candidate == "ffprobe.exe" || candidate == @".\ffprobe.exe");

        Assert.Null(found);
    }

    [Fact]
    public void On_linux_the_bare_command_name_is_used_as_is()
    {
        var found = MediaToolLocations.Which("ffprobe", "/usr/bin:/bin", windows: false, candidate => candidate == "/usr/bin/ffprobe");

        Assert.Equal("/usr/bin/ffprobe", found);
    }
}
