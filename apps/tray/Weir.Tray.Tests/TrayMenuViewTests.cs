using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The menu as Windows Forms draws it, from the description, and the hover text Windows accepts.</summary>
public sealed class TrayMenuViewTests
{
    private static IReadOnlyList<TrayMenuEntry> Describe(TrayState state, bool startsWithWindows = false) =>
        TrayMenu.Describe(new TrayMenuInputs(
            state,
            new UpdateMenuState("Check for updates", true, UpdateMenuAction.Check),
            LanAccessMenuState.Describe(ListenScope.ThisPcOnly, LanAccessActivity.Idle),
            null,
            startsWithWindows,
            "1.0.0-rc.10"));

    private static readonly TrayState Running = new(ServerPhase.Running, null, null, 9347);

    [Fact]
    public void The_menu_on_screen_has_the_described_items_in_order()
    {
        using var view = new TrayMenuView([], []);

        view.Show(Describe(Running));

        var described = Describe(Running).Select(entry => entry.Item is null ? "---" : entry.Text);
        var drawn = view.Strip.Items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "---" : item.Text);
        Assert.Equal(described, drawn);
    }

    [Fact]
    public void A_later_description_updates_the_items_instead_of_adding_new_ones()
    {
        using var view = new TrayMenuView([], []);
        view.Show(Describe(Running));
        var count = view.Strip.Items.Count;

        view.Show(Describe(new TrayState(ServerPhase.Stopped, null, null, 9347), startsWithWindows: true));

        Assert.Equal(count, view.Strip.Items.Count);
        Assert.Equal("Stopped - choose Restart Weir", view.Strip.Items[1].Text);
        Assert.False(view.Strip.Items[3].Enabled);
        Assert.Contains(view.Strip.Items.OfType<ToolStripMenuItem>(), item => item.Text == "Start with Windows" && item.Checked);
    }

    [Fact]
    public void Open_Weir_is_bold_and_Quit_carries_its_hover_text()
    {
        using var view = new TrayMenuView([], []);

        view.Show(Describe(Running));

        Assert.True(view.Strip.Items[0].Font.Bold);
        Assert.Equal(TrayMenu.QuitToolTip, view.Strip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Quit Weir").ToolTipText);
    }

    [Fact]
    public void A_click_runs_the_items_action_once()
    {
        var restarts = 0;
        using var view = new TrayMenuView(new() { [TrayMenuItem.Restart] = () => restarts++ }, []);
        view.Show(Describe(Running));
        view.Show(Describe(Running));

        view.Strip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Restart Weir").PerformClick();

        Assert.Equal(1, restarts);
    }

    [Fact]
    public void The_longest_hover_text_is_one_the_notification_area_accepts()
    {
        var status = new TrayStatus(false, null, 99, ["A connection with a rather long name that goes on and on and on"], true);
        var hover = new TrayState(ServerPhase.Running, status, "1.0.0-rc.10", 9347).HoverText;
        using var icon = new NotifyIcon();

        icon.Text = hover;

        Assert.True(hover.Length <= 127);
        Assert.Equal(hover, icon.Text);
    }
}
