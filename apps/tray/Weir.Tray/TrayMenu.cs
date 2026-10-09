using System.Globalization;
using Weir.Tray.LanAccess;

namespace Weir.Tray;

/// <summary>The tray menu's items, in the order they appear. Separators are not items.</summary>
enum TrayMenuItem
{
    Open,
    Status,
    Pause,
    Restart,
    CopyAddress,
    AllowOtherDevices,
    OnlyThisPc,
    ChangePort,
    OpenDataFolder,
    OpenLogsFolder,
    StartWithWindows,
    Update,
    Version,
    ReportProblem,
    Quit,
}

/// <summary>One line of the tray menu: a separator when <see cref="Item"/> is null, otherwise an item and how it looks.</summary>
sealed record TrayMenuEntry(TrayMenuItem? Item, string Text, bool Enabled = true, bool Bold = false, bool? Checked = null, string? ToolTip = null)
{
    internal static TrayMenuEntry Separator { get; } = new(null, string.Empty);
}

/// <summary>What the menu depends on.</summary>
/// <param name="State">The server, what it reports and the update, as shown on the icon.</param>
/// <param name="Update">The update item's text, enabled state and action.</param>
/// <param name="Lan">The two LAN access items' text and enabled state.</param>
/// <param name="MovingToPort">The port a move is under way to, or null.</param>
/// <param name="StartsWithWindows">Whether Weir starts when the person signs in to Windows.</param>
/// <param name="Version">Weir's version, without a leading "v".</param>
sealed record TrayMenuInputs(
    TrayState State,
    UpdateMenuState Update,
    LanAccessMenuState Lan,
    int? MovingToPort,
    bool StartsWithWindows,
    string Version);

/// <summary>
/// The tray menu as the standard lays it out (docs/tray-standard.md): what it holds, in what order, in which words, and
/// which items are enabled in each state. The tray draws exactly this; nothing else decides what the menu says.
/// </summary>
static class TrayMenu
{
    internal const string QuitToolTip = "Stops Weir after running jobs finish, then closes this icon. A downloaded update is installed.";

    internal static IReadOnlyList<TrayMenuEntry> Describe(TrayMenuInputs inputs)
    {
        var state = inputs.State;
        var running = state.Phase == ServerPhase.Running;
        return
        [
            new(TrayMenuItem.Open, "Open Weir", Bold: true),
            new(TrayMenuItem.Status, state.HoverText, Enabled: false),
            TrayMenuEntry.Separator,
            new(TrayMenuItem.Pause, state.IsPaused ? "Resume processing" : "Pause processing", Enabled: running),
            new(TrayMenuItem.Restart, "Restart Weir", Enabled: state.Phase is not (ServerPhase.Starting or ServerPhase.Stopping)),
            TrayMenuEntry.Separator,
            new(TrayMenuItem.CopyAddress, "Copy address"),
            new(TrayMenuItem.AllowOtherDevices, inputs.Lan.AllowText, inputs.Lan.AllowEnabled),
            new(TrayMenuItem.OnlyThisPc, inputs.Lan.ThisPcOnlyText, inputs.Lan.ThisPcOnlyEnabled),
            PortEntry(state.Port, inputs.MovingToPort),
            TrayMenuEntry.Separator,
            new(TrayMenuItem.OpenDataFolder, "Open data folder"),
            new(TrayMenuItem.OpenLogsFolder, "Open logs folder"),
            new(TrayMenuItem.StartWithWindows, "Start with Windows", Checked: inputs.StartsWithWindows),
            TrayMenuEntry.Separator,
            new(TrayMenuItem.Update, inputs.Update.Text, inputs.Update.Enabled),
            new(TrayMenuItem.Version, $"Weir v{inputs.Version}", Enabled: false),
            new(TrayMenuItem.ReportProblem, "Report a problem..."),
            TrayMenuEntry.Separator,
            new(TrayMenuItem.Quit, "Quit Weir", ToolTip: QuitToolTip),
        ];
    }

    private static TrayMenuEntry PortEntry(int port, int? movingTo) => movingTo is { } target
        ? new(TrayMenuItem.ChangePort, string.Create(CultureInfo.InvariantCulture, $"Moving to port {target}..."), Enabled: false)
        : new(TrayMenuItem.ChangePort, string.Create(CultureInfo.InvariantCulture, $"Change port ({port})..."));
}
