using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// What the icon shows and says: its one corner mark, chosen by priority, and the status line and hover text (docs/tray-standard.md).
/// </summary>
public sealed class TrayStateTests
{
    private static readonly TrayStatus AllWell = new(false, null, 0, [], true);

    private static TrayState Running(TrayStatus? server = null, string? update = null) =>
        new(ServerPhase.Running, server ?? AllWell, update, 9347);

    [Fact]
    public void All_well_has_no_mark()
    {
        Assert.Equal(TrayBadge.None, Running().Badge);
    }

    [Fact]
    public void Before_the_server_has_written_a_status_all_is_still_well()
    {
        Assert.Equal(TrayBadge.None, new TrayState(ServerPhase.Running, null, null, 9347).Badge);
    }

    [Fact]
    public void Starting_shows_the_grey_ring()
    {
        Assert.Equal(TrayBadge.Starting, new TrayState(ServerPhase.Starting, null, null, 9347).Badge);
    }

    [Fact]
    public void Paused_shows_the_two_bars()
    {
        Assert.Equal(TrayBadge.Paused, Running(AllWell with { Paused = true }).Badge);
    }

    [Fact]
    public void A_downloaded_update_shows_the_blue_dot()
    {
        Assert.Equal(TrayBadge.UpdateReady, Running(update: "1.0.0-rc.10").Badge);
    }

    [Fact]
    public void A_stopped_server_needs_the_person()
    {
        Assert.Equal(TrayBadge.NeedsYou, new TrayState(ServerPhase.Stopped, null, null, 9347).Badge);
    }

    [Fact]
    public void Files_waiting_on_the_person_need_them()
    {
        Assert.Equal(TrayBadge.NeedsYou, Running(AllWell with { FilesNeedingYou = 2 }).Badge);
    }

    [Fact]
    public void A_media_manager_that_cannot_be_reached_needs_the_person()
    {
        Assert.Equal(TrayBadge.NeedsYou, Running(AllWell with { ManagersUnreachable = ["Deluno"] }).Badge);
    }

    [Fact]
    public void A_server_that_says_it_is_not_well_needs_the_person()
    {
        Assert.Equal(TrayBadge.NeedsYou, Running(AllWell with { ServerOk = false }).Badge);
    }

    [Fact]
    public void Needing_the_person_wins_over_everything()
    {
        var everything = new TrayStatus(Paused: true, null, FilesNeedingYou: 1, [], ServerOk: true);

        Assert.Equal(TrayBadge.NeedsYou, Running(everything, update: "1.0.0").Badge);
        Assert.Equal(TrayBadge.NeedsYou, new TrayState(ServerPhase.Stopped, everything, "1.0.0", 9347).Badge);
    }

    [Fact]
    public void Starting_wins_over_paused_and_an_update()
    {
        var paused = AllWell with { Paused = true };

        Assert.Equal(TrayBadge.Starting, new TrayState(ServerPhase.Starting, paused, "1.0.0", 9347).Badge);
    }

    [Fact]
    public void Paused_wins_over_an_update()
    {
        Assert.Equal(TrayBadge.Paused, Running(AllWell with { Paused = true }, update: "1.0.0").Badge);
    }

    [Fact]
    public void What_a_server_wrote_before_it_stopped_or_restarted_is_not_shown()
    {
        var stale = new TrayStatus(Paused: true, null, FilesNeedingYou: 5, ["Deluno"], ServerOk: true);

        Assert.Equal(TrayBadge.Starting, new TrayState(ServerPhase.Starting, stale, null, 9347).Badge);
        Assert.Equal("Starting...", new TrayState(ServerPhase.Starting, stale, null, 9347).StatusLine);
        Assert.Equal("Stopped - choose Restart Weir", new TrayState(ServerPhase.Stopped, stale, null, 9347).StatusLine);
    }

    [Fact]
    public void Running_says_where_Weir_is()
    {
        Assert.Equal("Running at http://localhost:9347", Running().StatusLine);
        Assert.Equal("Weir - Running at http://localhost:9347", Running().HoverText);
    }

    [Fact]
    public void The_hover_text_is_the_product_name_and_the_status_line()
    {
        Assert.Equal("Weir - Starting...", new TrayState(ServerPhase.Starting, null, null, 9347).HoverText);
        Assert.Equal("Weir - Paused", Running(AllWell with { Paused = true }).HoverText);
        Assert.Equal("Weir - Update ready (1.0.0-rc.10)", Running(update: "1.0.0-rc.10").HoverText);
        Assert.Equal("Weir - Stopped - choose Restart Weir", new TrayState(ServerPhase.Stopped, null, null, 9347).HoverText);
    }

    [Fact]
    public void The_hover_text_says_what_needs_the_person()
    {
        Assert.Equal(
            "Weir - Running at http://localhost:9347 - 2 files need a look",
            Running(AllWell with { FilesNeedingYou = 2 }).HoverText);
        Assert.Equal(
            "Weir - Running at http://localhost:9347 - 1 file needs a look",
            Running(AllWell with { FilesNeedingYou = 1 }).HoverText);
        Assert.Equal(
            "Weir - Running at http://localhost:9347 - Deluno can't be reached",
            Running(AllWell with { ManagersUnreachable = ["Deluno"] }).HoverText);
        Assert.Equal(
            "Weir - Running at http://localhost:9347 - 3 files need a look - 2 media managers can't be reached",
            Running(AllWell with { FilesNeedingYou = 3, ManagersUnreachable = ["Deluno", "Sonarr"] }).HoverText);
    }

    [Fact]
    public void A_paused_Weir_still_says_what_needs_the_person()
    {
        Assert.Equal(
            "Weir - Paused - 4 files need a look",
            Running(AllWell with { Paused = true, FilesNeedingYou = 4 }).HoverText);
    }

    [Fact]
    public void A_server_that_is_not_well_points_to_the_logs()
    {
        Assert.Equal(
            "Weir - Needs attention - choose Open logs folder",
            Running(AllWell with { ServerOk = false }).HoverText);
    }

    [Fact]
    public void The_hover_text_never_passes_what_Windows_allows_and_is_cut_between_words()
    {
        const string longName = "Deluno on a machine in the back room of the house with a very long name indeed";
        var state = Running(AllWell with { FilesNeedingYou = 12, ManagersUnreachable = [longName] });

        var hover = state.HoverText;

        Assert.True(hover.Length <= TrayState.HoverLimit, hover);
        Assert.EndsWith("…", hover, StringComparison.Ordinal);
        Assert.StartsWith("Weir - Running at http://localhost:9347 - 12 files need a look - Deluno", hover, StringComparison.Ordinal);
        var full = $"Weir - {state.StatusLine}";
        var kept = hover[..^1];
        Assert.StartsWith(kept, full, StringComparison.Ordinal);
        Assert.Equal(' ', full[kept.Length]);
    }

    [Fact]
    public void A_hover_text_of_exactly_the_limit_is_left_alone()
    {
        var filler = new string('x', TrayState.HoverLimit - "Weir - Running at http://localhost:9347 - ".Length - " can't be reached".Length);
        var state = Running(AllWell with { ManagersUnreachable = [filler] });

        Assert.Equal(TrayState.HoverLimit, state.HoverText.Length);
        Assert.DoesNotContain("…", state.HoverText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_line_is_the_hover_text_without_the_product_name()
    {
        var state = Running(AllWell with { Paused = true, FilesNeedingYou = 1 }, update: "2.0.0");

        Assert.Equal($"Weir - {state.StatusLine}", state.HoverText);
    }

    [Fact]
    public void Copy_address_gives_this_pcs_address_until_other_devices_are_let_in()
    {
        Assert.Equal("http://localhost:9347", TrayState.AddressToCopy(ListenScope.ThisPcOnly, 9347, "MEDIA-PC"));
        Assert.Equal("http://MEDIA-PC:9347", TrayState.AddressToCopy(ListenScope.OtherDevices, 9347, "MEDIA-PC"));
    }
}
