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

    private static ToolStripMenuItem StartWithWindowsItem(TrayMenuView view) =>
        view.Strip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Start with Windows");

    // What UI Automation reads from the item, which Windows Forms keeps internal: whether it offers the Toggle pattern (10015)
    // and what that pattern's ToggleState says.
    private static (bool Supported, string? State) Toggle(AccessibleObject accessible)
    {
        const int togglePattern = 10015;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var supports = accessible.GetType().GetMethod("IsPatternSupported", flags)!;
        var supported = (bool)supports.Invoke(accessible, [Enum.ToObject(supports.GetParameters()[0].ParameterType, togglePattern)])!;
        return (supported, accessible.GetType().GetProperty("ToggleState", flags)?.GetValue(accessible)?.ToString());
    }

    [Fact]
    public void Start_with_Windows_is_a_real_check_item_whose_accessible_state_follows_the_description()
    {
        using var view = new TrayMenuView([], []);

        view.Show(Describe(Running, startsWithWindows: true));
        var item = StartWithWindowsItem(view);

        Assert.True(item.CheckOnClick);
        Assert.True(item.Checked);
        Assert.Equal(CheckState.Checked, item.CheckState);
        Assert.True(item.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));

        view.Show(Describe(Running, startsWithWindows: false));

        Assert.False(item.Checked);
        Assert.False(item.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
    }

    [Fact]
    public void Start_with_Windows_offers_the_Toggle_pattern_and_reports_its_toggle_state_to_UI_Automation()
    {
        using var view = new TrayMenuView([], []);
        view.Show(Describe(Running, startsWithWindows: true));
        var accessible = StartWithWindowsItem(view).AccessibilityObject;

        Assert.Equal((true, "ToggleState_On"), Toggle(accessible));

        view.Show(Describe(Running, startsWithWindows: false));

        Assert.Equal((true, "ToggleState_Off"), Toggle(accessible));
    }

    [Fact]
    public void A_plain_item_offers_no_Toggle_pattern()
    {
        using var view = new TrayMenuView([], []);
        view.Show(Describe(Running));

        var restart = view.Strip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Restart Weir");

        Assert.False(Toggle(restart.AccessibilityObject).Supported);
    }

    [Fact]
    public void Only_the_toggle_is_a_check_item()
    {
        using var view = new TrayMenuView([], []);

        view.Show(Describe(Running));

        Assert.Equal(["Start with Windows"], view.Strip.Items.OfType<ToolStripMenuItem>().Where(item => item.CheckOnClick).Select(item => item.Text));
    }

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
        Assert.Equal("Weir - Stopped - choose Restart Weir", view.Strip.Items[1].Text);
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
        var status = new TrayStatus(false, null, ["A connection with a rather long name that goes on and on and on"], ["The watched folder for a workflow with a long name too"], true);
        var hover = new TrayState(ServerPhase.Running, status, "1.0.0-rc.10", 9347).HoverText;
        using var icon = new NotifyIcon();

        icon.Text = hover;

        Assert.True(hover.Length <= 127);
        Assert.Equal(hover, icon.Text);
    }
}
