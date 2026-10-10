using System.Text.Json.Nodes;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>update-state.json is what System › About shows of the tray's update work, and what a failed step says in plain words.</summary>
public sealed class UpdateStateFileTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.Create();

    public void Dispose() => _home.Dispose();

    [Theory]
    [InlineData((int)UpdatePhase.Idle, "idle", false)]
    [InlineData((int)UpdatePhase.Checking, "checking", false)]
    [InlineData((int)UpdatePhase.Downloading, "downloading", false)]
    [InlineData((int)UpdatePhase.Downloaded, "downloaded", true)]
    [InlineData((int)UpdatePhase.Failed, "failed", false)]
    public void Each_phase_is_written_by_name_and_only_a_downloaded_one_says_downloaded(int phase, string name, bool downloaded)
    {
        UpdateStateFile.Write(_home.Path, (UpdatePhase)phase, "9.9.9", failure: "Not downloaded.");

        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(_home.Path, "update-state.json")))!;
        Assert.Equal((name, downloaded, "9.9.9", "Not downloaded."), (state["state"]!.GetValue<string>(), state["downloaded"]!.GetValue<bool>(), state["version"]!.GetValue<string>(), state["failure"]!.GetValue<string>()));
    }

    [Fact]
    public void The_file_is_replaced_whole_and_no_scratch_file_is_left_behind()
    {
        UpdateStateFile.Write(_home.Path, UpdatePhase.Checking, null);
        UpdateStateFile.Write(_home.Path, UpdatePhase.Idle, null);

        Assert.Equal(["update-state.json"], Directory.GetFileSystemEntries(_home.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failure_to_reach_GitHub_says_so_and_what_to_do()
    {
        Assert.Equal(
            "Weir could not reach GitHub to look for an update. Check the internet connection and try again.",
            UpdateFailureText.ForCheck(new HttpRequestException("No such host is known.")));
        Assert.Equal(
            "Weir could not download the update. Check the internet connection and try again.",
            UpdateFailureText.ForDownload(new TaskCanceledException()));
    }

    [Fact]
    public void A_download_that_cannot_be_saved_points_at_the_disk()
    {
        Assert.Equal(
            "Weir could not save the update on this PC. Check there is free disk space and try again.",
            UpdateFailureText.ForDownload(new IOException("There is not enough space on the disk.")));
    }

    [Fact]
    public void Anything_else_is_not_shown_as_the_exception_text_and_points_at_the_logs()
    {
        var text = UpdateFailureText.ForCheck(new InvalidOperationException("Secret internal detail"));

        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        Assert.Contains("Open logs folder", text, StringComparison.Ordinal);
    }
}
