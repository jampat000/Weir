using Weir.Tray.LanAccess;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// A server that cannot start says why in startup-error.txt, and the tray shows that in its hover text and balloon instead of a
/// bare "couldn't start"; one that is busy before it can answer says so in startup-progress.txt, and is waited for (#951).
/// </summary>
public sealed class StartupErrorTests : IDisposable
{
    private const string Headline = "Couldn't save a copy of its data before updating";
    private const string Sentence =
        "Weir couldn't save a copy of its data before updating, so it didn't change anything: Drive C: needs about 480 MB free to hold the copy and has 120 MB. Free up space there, then try again.";

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    // Well before the notes the tests write: the file system stamps files with a coarser clock than DateTime.UtcNow reads.
    private readonly DateTime _serverStarted = DateTime.UtcNow - TimeSpan.FromMinutes(1);

    public void Dispose() => _home.Dispose();

    private string ErrorPath => Path.Combine(_home.Path, StartupNotes.ErrorFileName);

    private string ProgressPath => Path.Combine(_home.Path, StartupNotes.ProgressFileName);

    [Fact]
    public void The_reason_is_a_headline_then_the_whole_sentence()
    {
        File.WriteAllText(ErrorPath, $"{Headline}\n{Sentence}");

        Assert.Equal(new StartupError(Headline, Sentence), StartupNotes.ReadError(_home.Path, _serverStarted));
    }

    [Fact]
    public void A_file_with_only_a_headline_uses_it_for_the_balloon_too()
    {
        File.WriteAllText(ErrorPath, Headline);

        Assert.Equal(new StartupError(Headline, Headline), StartupNotes.ReadError(_home.Path, _serverStarted));
    }

    [Fact]
    public void A_reason_the_last_server_left_is_not_this_ones()
    {
        File.WriteAllText(ErrorPath, $"{Headline}\n{Sentence}");
        File.SetLastWriteTimeUtc(ErrorPath, _serverStarted - TimeSpan.FromMinutes(10));

        Assert.Null(StartupNotes.ReadError(_home.Path, _serverStarted));
    }

    [Fact]
    public void No_file_and_an_empty_file_are_no_reason()
    {
        Assert.Null(StartupNotes.ReadError(_home.Path, _serverStarted));

        File.WriteAllText(ErrorPath, "  \n");

        Assert.Null(StartupNotes.ReadError(_home.Path, _serverStarted));
    }

    [Fact]
    public void A_server_is_busy_while_its_progress_note_exists_and_is_recent()
    {
        var now = DateTime.UtcNow;
        Assert.False(StartupNotes.IsBusy(_home.Path, _serverStarted, now));

        File.WriteAllText(ProgressPath, "Saving a copy of Weir's data (480 MB) before updating…");

        Assert.True(StartupNotes.IsBusy(_home.Path, _serverStarted, now));
        Assert.False(StartupNotes.IsBusy(_home.Path, _serverStarted, now + StartupNotes.ProgressFreshFor + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_copy_that_goes_on_for_hours_keeps_the_tray_waiting_while_its_note_is_touched()
    {
        File.WriteAllText(ProgressPath, "Saving a copy");
        var now = _serverStarted + TimeSpan.FromHours(3);

        // The server touches the note about every ten seconds; each look finds it a few seconds old.
        File.SetLastWriteTimeUtc(ProgressPath, now - TimeSpan.FromSeconds(10));

        Assert.True(StartupNotes.IsBusy(_home.Path, _serverStarted, now));
    }

    [Fact]
    public void A_hung_copy_whose_note_has_gone_quiet_shows_as_a_problem_within_a_couple_of_minutes()
    {
        File.WriteAllText(ProgressPath, "Saving a copy");
        var now = _serverStarted + TimeSpan.FromMinutes(20);
        File.SetLastWriteTimeUtc(ProgressPath, now - TimeSpan.FromMinutes(3));

        Assert.False(StartupNotes.IsBusy(_home.Path, _serverStarted, now));
        Assert.True(StartupNotes.ProgressFreshFor <= TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void A_note_written_the_instant_before_the_server_began_is_the_last_ones_not_this_ones()
    {
        File.WriteAllText(ErrorPath, $"{Headline}\n{Sentence}");
        File.SetLastWriteTimeUtc(ErrorPath, _serverStarted - TimeSpan.FromMilliseconds(1));
        File.WriteAllText(ProgressPath, "busy");
        File.SetLastWriteTimeUtc(ProgressPath, _serverStarted - TimeSpan.FromMilliseconds(1));

        Assert.Null(StartupNotes.ReadError(_home.Path, _serverStarted));
        Assert.False(StartupNotes.IsBusy(_home.Path, _serverStarted, _serverStarted));

        File.SetLastWriteTimeUtc(ErrorPath, _serverStarted);

        Assert.NotNull(StartupNotes.ReadError(_home.Path, _serverStarted));
    }

    [Fact]
    public void A_progress_note_the_last_server_left_does_not_keep_this_one_waiting()
    {
        File.WriteAllText(ProgressPath, "busy");
        File.SetLastWriteTimeUtc(ProgressPath, _serverStarted - TimeSpan.FromMinutes(10));

        Assert.False(StartupNotes.IsBusy(_home.Path, _serverStarted, DateTime.UtcNow));
    }

    [Fact]
    public void The_hover_text_of_a_stopped_server_gives_the_reason_within_the_limit()
    {
        var state = new TrayState(ServerPhase.Stopped, null, null, 9347, StartupError: new StartupError(Headline, Sentence));

        Assert.Equal("Weir - Stopped - Couldn't save a copy of its data before updating - choose Restart Weir", state.HoverText);
        Assert.True(state.HoverText.Length <= TrayState.HoverLimit);
        Assert.Equal(TrayDot.Red, state.Dot);
    }

    [Fact]
    public void A_stopped_server_that_gave_no_reason_is_worded_as_before()
    {
        var state = new TrayState(ServerPhase.Stopped, null, null, 9347);

        Assert.Equal("Weir - Stopped - choose Restart Weir", state.HoverText);
    }

    [Fact]
    public void The_balloons_carry_the_reason_and_the_way_back()
    {
        var why = new StartupError(Headline, Sentence);

        Assert.Equal($"Weir couldn't start. {Sentence} Click to try again.", TrayBalloons.CouldNotStartBecause(why));
        Assert.Equal($"Weir stopped. {Sentence} Click to try again.", TrayBalloons.StoppedBecause(why));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_server_that_cannot_start_leaves_its_reason_for_the_tray_to_show()
    {
        using var host = HostWithServerThatCannotStart(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None));

        Assert.Equal(ServerPhase.Stopped, host.Phase);
        Assert.Equal(new StartupError(Headline, Sentence), host.StartupError);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_watchdog_tries_a_server_that_said_why_it_cannot_start_once_more_and_then_stops()
    {
        using var host = HostWithServerThatCannotStart(out var startsPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None));
        var gaveUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.Watch(() => gaveUp.TrySetResult(), CancellationToken.None);
        await gaveUp.Task.WaitAsync(TimeSpan.FromSeconds(50));

        // The first start, and the one retry; not five.
        Assert.Equal(2, File.ReadAllLines(startsPath).Length);
        Assert.Equal(ServerPhase.Stopped, host.Phase);
        Assert.Equal(new StartupError(Headline, Sentence), host.StartupError);
    }

    // A real ServerHost over a stand-in WeirServer.exe that writes startup-error.txt and exits, counting its starts.
    private ServerHost HostWithServerThatCannotStart(out string startsPath)
    {
        var installRoot = Path.Combine(_home.Path, "install");
        var serverFolder = Path.Combine(installRoot, "server");
        StandInServers.Place(serverFolder, "WeirServer.exe");
        File.WriteAllText(Path.Combine(serverFolder, "fail-start.txt"), $"{Headline}\n{Sentence}");
        startsPath = Path.Combine(serverFolder, "starts.txt");
        return new ServerHost(_home.Path, installRoot, port: 59347, ListenScope.ThisPcOnly);
    }
}
