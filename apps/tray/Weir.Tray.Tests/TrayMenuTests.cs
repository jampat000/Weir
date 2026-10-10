using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The tray menu: what it holds, in what order and words, and which items are enabled in each state (docs/tray-standard.md).</summary>
public sealed class TrayMenuTests
{
    private static readonly TrayStatus AllWell = new(false, null, [], [], true);
    private static readonly UpdateMenuState CheckForUpdates = new("Check for updates", true, UpdateMenuAction.Check);
    private static readonly LanAccessMenuState LanIdle = LanAccessMenuState.Describe(ListenScope.ThisPcOnly, LanAccessActivity.Idle);

    private static IReadOnlyList<TrayMenuEntry> Menu(
        ServerPhase phase = ServerPhase.Running,
        TrayStatus? server = null,
        string? update = null,
        UpdateMenuState? updateItem = null,
        LanAccessMenuState? lan = null,
        int? movingToPort = null,
        bool startsWithWindows = false) =>
        TrayMenu.Describe(new TrayMenuInputs(
            new TrayState(phase, server ?? AllWell, update, 9347),
            updateItem ?? CheckForUpdates,
            lan ?? LanIdle,
            movingToPort,
            startsWithWindows,
            "1.0.0-rc.10"));

    private static TrayMenuEntry Item(IReadOnlyList<TrayMenuEntry> menu, TrayMenuItem item) => menu.Single(entry => entry.Item == item);

    [Fact]
    public void The_menu_holds_these_items_in_this_order_with_separators_between_the_groups()
    {
        var lines = Menu().Select(entry => entry.Item is null ? "---" : entry.Text).ToList();

        Assert.Equal(
            [
                "Open Weir",
                "Weir - Running at http://localhost:9347",
                "---",
                "Pause processing",
                "Restart Weir",
                "---",
                "Copy address",
                "Allow other devices on your network...",
                "Only allow this PC",
                "Change port (9347)...",
                "---",
                "Open data folder",
                "Open logs folder",
                "Start with Windows",
                "---",
                "Check for updates",
                "Weir v1.0.0-rc.10",
                "Report a problem...",
                "---",
                "Quit Weir",
            ],
            lines);
    }

    [Fact]
    public void There_is_no_stop_or_start_item()
    {
        var texts = Menu(ServerPhase.Stopped).Where(entry => entry.Item != TrayMenuItem.Status).Select(entry => entry.Text).ToList();

        Assert.DoesNotContain(texts, text => text.StartsWith("Stop", StringComparison.Ordinal) || text.StartsWith("Start Weir", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_Weir_is_bold_and_the_only_bold_item()
    {
        var menu = Menu();

        Assert.True(Item(menu, TrayMenuItem.Open).Bold);
        Assert.Single(menu, entry => entry.Bold);
    }

    [Fact]
    public void The_status_and_version_lines_are_greyed_out()
    {
        var menu = Menu();

        Assert.False(Item(menu, TrayMenuItem.Status).Enabled);
        Assert.False(Item(menu, TrayMenuItem.Version).Enabled);
    }

    [Fact]
    public void The_status_line_has_the_same_words_as_the_hover_text()
    {
        var server = AllWell with { Paused = true, ManagersUnreachable = ["Deluno"] };
        var state = new TrayState(ServerPhase.Running, server, null, 9347);

        Assert.Equal(state.HoverText, Item(Menu(server: server), TrayMenuItem.Status).Text);
        Assert.StartsWith("Weir - Paused - Deluno isn't answering", Item(Menu(server: server), TrayMenuItem.Status).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_line_starts_with_the_product_name_in_every_state()
    {
        var states = new[]
        {
            new TrayState(ServerPhase.Running, AllWell, null, 9347),
            new TrayState(ServerPhase.Running, AllWell, "1.0.0", 9347),
            new TrayState(ServerPhase.Running, AllWell with { ManagersUnreachable = ["Deluno"] }, null, 9347),
            new TrayState(ServerPhase.Running, AllWell with { FoldersUnreachable = ["The watched folder for Movies"] }, null, 9347),
            new TrayState(ServerPhase.Starting, null, null, 9347),
            new TrayState(ServerPhase.Stopped, null, null, 9347),
        };

        foreach (var state in states)
        {
            var text = Item(Menu(phase: state.Phase, server: state.Server, update: state.UpdateVersion), TrayMenuItem.Status).Text;

            Assert.Equal(state.HoverText, text);
            Assert.StartsWith("Weir - ", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Quit_says_what_it_stops()
    {
        var quit = Item(Menu(), TrayMenuItem.Quit);

        Assert.Equal("Quit Weir", quit.Text);
        Assert.Equal("Stops Weir after running jobs finish, then closes this icon. A downloaded update is installed.", quit.ToolTip);
        Assert.Single(Menu(), entry => entry.ToolTip is not null);
    }

    [Fact]
    public void Pause_becomes_Resume_while_processing_is_paused()
    {
        Assert.Equal("Pause processing", Item(Menu(), TrayMenuItem.Pause).Text);
        Assert.Equal("Resume processing", Item(Menu(server: AllWell with { Paused = true }), TrayMenuItem.Pause).Text);
    }

    [Fact]
    public void Pause_works_whenever_the_server_runs_even_before_it_has_written_a_status()
    {
        var withoutStatus = TrayMenu.Describe(new TrayMenuInputs(
            new TrayState(ServerPhase.Running, null, null, 9347), CheckForUpdates, LanIdle, null, false, "1.0.0"));

        Assert.True(Item(Menu(), TrayMenuItem.Pause).Enabled);
        Assert.True(Item(withoutStatus, TrayMenuItem.Pause).Enabled);
        Assert.Equal("Pause processing", Item(withoutStatus, TrayMenuItem.Pause).Text);
    }

    [Fact]
    public void Pause_waits_while_the_server_is_not_running()
    {
        Assert.False(Item(Menu(ServerPhase.Starting), TrayMenuItem.Pause).Enabled);
        Assert.False(Item(Menu(ServerPhase.Stopped), TrayMenuItem.Pause).Enabled);
    }

    [Fact]
    public void Restart_is_the_way_back_from_a_stopped_server_and_waits_while_one_is_starting()
    {
        Assert.True(Item(Menu(ServerPhase.Running), TrayMenuItem.Restart).Enabled);
        Assert.True(Item(Menu(ServerPhase.Stopped), TrayMenuItem.Restart).Enabled);
        Assert.False(Item(Menu(ServerPhase.Starting), TrayMenuItem.Restart).Enabled);
        Assert.False(Item(Menu(ServerPhase.Stopping), TrayMenuItem.Restart).Enabled);
        Assert.Equal("Weir - Stopping...", Item(Menu(ServerPhase.Stopping), TrayMenuItem.Status).Text);
    }

    [Fact]
    public void A_stopped_server_says_so_in_the_status_line()
    {
        Assert.Equal("Weir - Stopped - choose Restart Weir", Item(Menu(ServerPhase.Stopped), TrayMenuItem.Status).Text);
    }

    [Fact]
    public void Open_the_folders_report_a_problem_and_copy_address_are_always_available()
    {
        foreach (var phase in new[] { ServerPhase.Starting, ServerPhase.Running, ServerPhase.Stopped })
        {
            var menu = Menu(phase);
            foreach (var item in new[] { TrayMenuItem.Open, TrayMenuItem.CopyAddress, TrayMenuItem.OpenDataFolder, TrayMenuItem.OpenLogsFolder, TrayMenuItem.StartWithWindows, TrayMenuItem.ReportProblem, TrayMenuItem.Quit })
            {
                Assert.True(Item(menu, item).Enabled, $"{item} while {phase}");
            }
        }
    }

    [Fact]
    public void Start_with_Windows_is_ticked_when_Weir_starts_with_Windows()
    {
        Assert.False(Item(Menu(startsWithWindows: false), TrayMenuItem.StartWithWindows).Checked);
        Assert.True(Item(Menu(startsWithWindows: true), TrayMenuItem.StartWithWindows).Checked);
        Assert.Single(Menu(startsWithWindows: true), entry => entry.Checked is not null);
    }

    [Fact]
    public void The_update_item_follows_the_update_state()
    {
        var checking = new UpdateMenuState("Checking for updates...", false, UpdateMenuAction.Wait);
        var restart = new UpdateMenuState("Restart to update (v1.0.0)", true, UpdateMenuAction.Restart);

        Assert.Equal(("Checking for updates...", false), (Item(Menu(updateItem: checking), TrayMenuItem.Update).Text, Item(Menu(updateItem: checking), TrayMenuItem.Update).Enabled));
        Assert.Equal(("Restart to update (v1.0.0)", true), (Item(Menu(updateItem: restart), TrayMenuItem.Update).Text, Item(Menu(updateItem: restart), TrayMenuItem.Update).Enabled));
    }

    [Fact]
    public void The_lan_items_follow_the_lan_state()
    {
        var waiting = LanAccessMenuState.Describe(ListenScope.ThisPcOnly, LanAccessActivity.WaitingForWindows);
        var open = LanAccessMenuState.Describe(ListenScope.OtherDevices, LanAccessActivity.Idle);

        var whileWaiting = Menu(lan: waiting);
        var whenOpen = Menu(lan: open);

        Assert.Equal(("Waiting for Windows admin approval...", false), (Item(whileWaiting, TrayMenuItem.AllowOtherDevices).Text, Item(whileWaiting, TrayMenuItem.AllowOtherDevices).Enabled));
        Assert.False(Item(Menu(), TrayMenuItem.OnlyThisPc).Enabled);
        Assert.True(Item(whenOpen, TrayMenuItem.OnlyThisPc).Enabled);
        Assert.True(Item(whenOpen, TrayMenuItem.AllowOtherDevices).Enabled);
    }

    [Fact]
    public void Change_port_shows_the_port_and_while_it_moves_says_where_to()
    {
        var moving = Item(Menu(movingToPort: 9400), TrayMenuItem.ChangePort);

        Assert.Equal("Change port (9347)...", Item(Menu(), TrayMenuItem.ChangePort).Text);
        Assert.True(Item(Menu(), TrayMenuItem.ChangePort).Enabled);
        Assert.Equal(("Moving to port 9400...", false), (moving.Text, moving.Enabled));
    }
}
