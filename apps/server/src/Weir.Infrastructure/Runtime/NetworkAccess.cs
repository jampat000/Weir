using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Weir.Infrastructure.Runtime;

/// <summary>Whether another device on the network can reach Weir right now, as System › About shows it.</summary>
public enum NetworkAccessState
{
    /// <summary>Not the Windows package: Docker and a bare source install manage their own network exposure.</summary>
    NotApplicable,

    /// <summary>No rule and no block: nobody has answered the first-run "Allow Weir on your network?" prompt yet.</summary>
    NotConfigured,

    /// <summary>Weir's allow rule covers the network this machine is on, and nothing blocks it.</summary>
    Allowed,

    /// <summary>Either a block rule targets Weir's server, or its allow rule does not cover the current network.</summary>
    Blocked,
}

/// <summary>
/// Reads whether Weir can be reached from the network, for System › About. Registered by
/// <c>WeirPlatformServices.AddWeirPlatform</c>: the Windows build reads the real firewall state, every other
/// platform reports <see cref="NetworkAccessState.NotApplicable"/>.
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

/// <summary>
/// The Windows package's read of Weir's own firewall rule, through the same COM policy object the tray writes to
/// (<c>HNetCfg.FwPolicy2</c> / <c>INetFwPolicy2</c> — see <c>apps/tray/Weir.Tray/Firewall/ComFirewallPolicy.cs</c>).
/// Reading a rule and the active network profile needs no administrator rights.
///
/// This does not share code with the tray's writer: <c>apps/server</c> and <c>apps/tray</c> are separate
/// solutions with no shared project reference, and only the tray ever creates or removes the rule. Keep the rule
/// name and program path in step with <c>WeirFirewallRule</c> there if either changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkAccessReader : INetworkAccessReader
{
    private const string RuleName = "Weir";
    private const string ServerExeName = "WeirServer.exe";

    // NET_FW_RULE_DIRECTION_IN.
    private const int DirectionIn = 1;

    // NET_FW_ACTION_.
    private const int ActionAllow = 1;

    public NetworkAccessState ReadState()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException($"{nameof(WindowsNetworkAccessReader)} is Windows-only.");
        }

        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The COM policy object could not be reached (a locked-down machine, a firewall service that is not
            // running): read as "not configured" rather than surfacing a COM failure as an operator-facing state.
            return NetworkAccessState.NotConfigured;
        }
    }

    private static NetworkAccessState Read()
    {
        var programPath = Path.Combine(AppContext.BaseDirectory, ServerExeName);
        dynamic policy = CreateComObject("HNetCfg.FwPolicy2");
        var currentProfiles = (int)policy.CurrentProfileTypes;

        var hasOwnBlockRule = false;
        FirewallRuleMatch? allowRule = null;
        foreach (dynamic comRule in policy.Rules)
        {
            var match = TryMatch(comRule, programPath);
            if (match is null)
            {
                continue;
            }
            if (!match.Value.IsAllow)
            {
                hasOwnBlockRule = true;
                continue;
            }
            if (string.Equals(match.Value.Name, RuleName, StringComparison.Ordinal))
            {
                allowRule = match.Value;
            }
        }

        if (hasOwnBlockRule)
        {
            return NetworkAccessState.Blocked;
        }
        if (allowRule is not { } configuredRule)
        {
            return NetworkAccessState.NotConfigured;
        }

        var coversCurrentNetwork = (configuredRule.Profiles & currentProfiles) != 0;
        return configuredRule.Enabled && coversCurrentNetwork ? NetworkAccessState.Allowed : NetworkAccessState.Blocked;
    }

    // Only inbound rules for Weir's own server exe are meaningful here; everything else reads as null.
    private static FirewallRuleMatch? TryMatch(dynamic comRule, string programPath)
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
            return new FirewallRuleMatch((string)comRule.Name, (int)comRule.Action == ActionAllow, (bool)comRule.Enabled, (int)comRule.Profiles);
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

    private readonly record struct FirewallRuleMatch(string Name, bool IsAllow, bool Enabled, int Profiles);
}
