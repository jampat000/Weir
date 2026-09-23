namespace Weir.Tray;

/// <summary>What clicking the tray's update menu item does.</summary>
enum UpdateMenuAction
{
    /// <summary>Not a Velopack install (a development build): open System › About in the browser instead.</summary>
    OpenUpdatePage,
    Check,
    Download,
    Restart,

    /// <summary>A check or download is running; the item is disabled.</summary>
    Wait,
}
