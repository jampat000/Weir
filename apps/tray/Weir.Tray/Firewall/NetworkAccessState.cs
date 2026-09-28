namespace Weir.Tray.Firewall;

/// <summary>Whether another device on the network can reach Weir right now, as System › About shows it.</summary>
enum NetworkAccessState
{
    /// <summary>No rule and no block: nobody has asked Windows Firewall for or against LAN access yet.</summary>
    NotConfigured,

    /// <summary>Weir's allow rule covers the network this machine is on, and nothing blocks it.</summary>
    Allowed,

    /// <summary>Either a block rule targets Weir's server, or its allow rule does not cover the current network.</summary>
    Blocked,
}
