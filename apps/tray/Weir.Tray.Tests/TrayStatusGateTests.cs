using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The server leaves tray-status.json behind when it stops, saying it is not well, and the tray reads it at launch. Only what
/// the server the tray started wrote is believed.
/// </summary>
public sealed class TrayStatusGateTests
{
    private static readonly DateTime ServerStarted = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private static TrayStatusReading Wrote(DateTime at, bool serverOk = true) =>
        new(new TrayStatus(false, null, [], [], serverOk), at, Failed: false);

    private static TrayStatusGate Gate(DateTime? serverStarted = null) => new(() => serverStarted ?? ServerStarted);

    [Fact]
    public void A_file_the_last_run_left_behind_is_not_believed()
    {
        var gate = Gate();

        gate.Offer(Wrote(ServerStarted.AddMinutes(-5), serverOk: false));

        Assert.Null(gate.Current);
    }

    [Fact]
    public void A_file_the_server_wrote_after_it_started_is_believed()
    {
        var gate = Gate();

        gate.Offer(Wrote(ServerStarted.AddSeconds(2)));

        Assert.NotNull(gate.Current);
    }

    [Fact]
    public void A_file_written_at_the_very_moment_the_server_started_is_believed()
    {
        var gate = Gate();

        gate.Offer(Wrote(ServerStarted));

        Assert.NotNull(gate.Current);
    }

    [Fact]
    public void Nothing_is_believed_before_a_server_has_been_started()
    {
        var gate = new TrayStatusGate(() => null);

        gate.Offer(Wrote(DateTime.UtcNow));

        Assert.Null(gate.Current);
    }

    [Fact]
    public void A_server_starting_again_forgets_what_the_one_before_said()
    {
        var gate = Gate();
        gate.Offer(Wrote(ServerStarted.AddSeconds(2)));

        gate.ServerStarting();

        Assert.Null(gate.Current);
    }

    [Fact]
    public void The_file_the_stopping_server_wrote_does_not_show_once_the_next_one_has_started()
    {
        var started = ServerStarted;
        var gate = new TrayStatusGate(() => started);
        gate.Offer(Wrote(started.AddSeconds(2)));
        var leftBehind = Wrote(started.AddMinutes(30), serverOk: false);

        gate.ServerStarting();
        started = started.AddMinutes(31);
        gate.Offer(leftBehind);

        Assert.Null(gate.Current);
        Assert.Equal(TrayDot.Starting, new TrayState(ServerPhase.Running, gate.Current, null, 9347).Dot);
    }

    [Fact]
    public void A_file_that_has_gone_means_no_status()
    {
        var gate = Gate();
        gate.Offer(Wrote(ServerStarted.AddSeconds(2)));

        gate.Offer(TrayStatusReading.Missing);

        Assert.Null(gate.Current);
    }

    [Fact]
    public void A_newer_file_replaces_the_one_before()
    {
        var gate = Gate();
        gate.Offer(Wrote(ServerStarted.AddSeconds(2)));

        gate.Offer(Wrote(ServerStarted.AddSeconds(9), serverOk: false));

        Assert.False(gate.Current!.ServerOk);
    }
}
