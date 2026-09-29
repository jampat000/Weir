using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Weir.Core.Configuration;

namespace Weir.Infrastructure.Runtime;

/// <summary>Whether another device on the network can reach Weir right now, as System › About shows it.</summary>
public enum NetworkAccessState
{
    /// <summary>Not the Windows package: Docker and a bare source install manage their own network exposure.</summary>
    NotApplicable,

    /// <summary>The server only accepts connections from this PC, so the firewall is not consulted.</summary>
    ThisPcOnly,

    /// <summary>The server accepts other devices, and Windows Firewall has an allow rule that covers the network this PC is on.</summary>
    Allowed,

    /// <summary>The server accepts other devices, but Windows Firewall does not let them through.</summary>
    Blocked,
}

/// <summary>
/// Reads whether Weir can be reached from the network, for System › About. Registered by
/// <c>WeirPlatformServices.AddWeirPlatform</c>: the Windows build reads its own bind address and the real firewall
/// state, every other platform reports <see cref="NetworkAccessState.NotApplicable"/>.
/// </summary>
public interface INetworkAccessReader
{
    NetworkAccessState ReadState();
}

/// <inheritdoc cref="INetworkAccessReader"/>
public sealed class UnsupportedNetworkAccessReader : INetworkAccessReader
{
    public NetworkAccessState ReadState() => NetworkAccessState.NotApplicable;
}

/// <summary>An inbound firewall rule for Weir's own server program, as much of it as the state decision needs.</summary>
internal readonly record struct ServerFirewallRule(bool IsAllow, bool Enabled, int Profiles);

/// <summary>
/// What System › About says, worked out from where the server listens and the firewall rules for its program.
/// Pure, so the Windows-only COM read stays out of it.
/// </summary>
internal static class NetworkAccessDecision
{
    /// <summary>
    /// Other devices get in when an enabled allow rule covers a network this PC is on and no enabled block rule
    /// does: a block wins, as in Windows Firewall itself. Weir's own "Weir" rule and one Windows made when someone
    /// clicked Allow on its prompt count the same.
    /// </summary>
    internal static NetworkAccessState Decide(bool listensOnThisPcOnly, IReadOnlyList<ServerFirewallRule> rules, int currentProfiles)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (listensOnThisPcOnly)
        {
            return NetworkAccessState.ThisPcOnly;
        }

        var enabled = rules.Where(rule => rule.Enabled && (rule.Profiles & currentProfiles) != 0).ToList();
        var allowed = enabled.Any(rule => rule.IsAllow) && !enabled.Any(rule => !rule.IsAllow);
        return allowed ? NetworkAccessState.Allowed : NetworkAccessState.Blocked;
    }
}

/// <summary>
/// The Windows package's read of Weir's own firewall rules, through the same COM policy object the tray writes to
/// (<c>HNetCfg.FwPolicy2</c> / <c>INetFwPolicy2</c> — see <c>apps/tray/Weir.Tray/Firewall/ComFirewallPolicy.cs</c>).
/// Reading a rule and the active network profile needs no administrator rights.
///
/// This does not share code with the tray's writer: <c>apps/server</c> and <c>apps/tray</c> are separate
/// solutions with no shared project reference, and only the tray ever creates or removes the rule. Keep the
/// program path in step with <c>WeirFirewallRule</c> there if it changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkAccessReader : INetworkAccessReader
{
    private const string ServerExeName = "WeirServer.exe";

    // NET_FW_RULE_DIRECTION_IN.
    private const int DirectionIn = 1;

    // NET_FW_ACTION_.
    private const int ActionAllow = 1;

    private readonly ServerListenOptions _listen;

    public WindowsNetworkAccessReader(ServerListenOptions listen)
    {
        _listen = listen ?? throw new ArgumentNullException(nameof(listen));
    }

    public NetworkAccessState ReadState()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException($"{nameof(WindowsNetworkAccessReader)} is Windows-only.");
        }

        if (_listen.IsThisPcOnly)
        {
            return NetworkAccessState.ThisPcOnly;
        }

        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The COM policy object could not be reached (a locked-down machine, a firewall service that is not
            // running): the firewall's answer is unknown, so this reports what Weir can prove, that no allow rule
            // was seen.
            return NetworkAccessState.Blocked;
        }
    }

    private static NetworkAccessState Read()
    {
        var programPath = Path.Combine(AppContext.BaseDirectory, ServerExeName);
        dynamic policy = CreateComObject("HNetCfg.FwPolicy2");
        var currentProfiles = (int)policy.CurrentProfileTypes;

        var rules = new List<ServerFirewallRule>();
        foreach (dynamic comRule in policy.Rules)
        {
            if (TryRead(comRule, programPath) is { } rule)
            {
                rules.Add(rule);
            }
        }
        return NetworkAccessDecision.Decide(listensOnThisPcOnly: false, rules, currentProfiles);
    }

    // Only inbound rules for Weir's own server exe are meaningful here; everything else reads as null.
    private static ServerFirewallRule? TryRead(dynamic comRule, string programPath)
    {
        try
        {
            if ((int)comRule.Direction != DirectionIn)
            {
                return null;
            }
            string? applicationName = comRule.ApplicationName;
            if (!string.Equals(applicationName, programPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return new ServerFirewallRule((int)comRule.Action == ActionAllow, (bool)comRule.Enabled, (int)comRule.Profiles);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static dynamic CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"Windows Firewall's COM object '{progId}' is not registered on this machine.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create Windows Firewall's COM object '{progId}'.");
    }
}
