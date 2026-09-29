namespace Weir.Tray.LanAccess;

/// <summary>What the tray is doing about LAN access at the moment.</summary>
enum LanAccessActivity
{
    Idle,
    WaitingForWindows,
    RestartingForOtherDevices,
    RestartingForThisPcOnly,
}

/// <summary>
/// The text and enabled state of the tray's two LAN access items. "Allow other devices" stays available while
/// they are already allowed, because running it again puts back a firewall rule that was removed.
/// </summary>
sealed record LanAccessMenuState(string AllowText, bool AllowEnabled, string ThisPcOnlyText, bool ThisPcOnlyEnabled)
{
    internal const string AllowLabel = "Allow other devices on your network...";
    internal const string ThisPcOnlyLabel = "Only allow this PC";

    internal static LanAccessMenuState Describe(ListenScope scope, LanAccessActivity activity) => activity switch
    {
        LanAccessActivity.WaitingForWindows => new("Waiting for Windows admin approval...", false, ThisPcOnlyLabel, false),
        LanAccessActivity.RestartingForOtherDevices => new("Restarting Weir for other devices...", false, ThisPcOnlyLabel, false),
        LanAccessActivity.RestartingForThisPcOnly => new(AllowLabel, false, "Restarting Weir for this PC only...", false),
        _ => new(AllowLabel, true, ThisPcOnlyLabel, scope == ListenScope.OtherDevices),
    };
}
