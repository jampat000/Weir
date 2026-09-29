namespace Weir.Tray.Firewall;

/// <summary>Exit codes for <c>--configure-firewall</c>, <c>--remove-firewall</c> and <c>--allow-lan</c>.</summary>
static class FirewallExitCode
{
    internal const int Success = 0;

    /// <summary>Matches Win32's own ERROR_ACCESS_DENIED: this process is not running as administrator.</summary>
    internal const int NotElevated = 5;

    /// <summary>The Windows Firewall COM API rejected the read or write.</summary>
    internal const int FirewallApiError = 1;

    /// <summary><c>--allow-lan</c> could not write the LAN access choice to Weir's data folder.</summary>
    internal const int LanAccessNotSaved = 2;
}
