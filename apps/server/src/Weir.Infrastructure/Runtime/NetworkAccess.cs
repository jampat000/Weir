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

/// <summary>Who the Windows package's server accepts connections from.</summary>
public enum NetworkScope
{
    /// <summary>Only this PC: the server listens on the loopback addresses.</summary>
    ThisPcOnly,

    /// <summary>Every device that can reach this PC, subject to Windows Firewall.</summary>
    Network,
}

/// <summary>What Windows Firewall says about Weir's server program.</summary>
public enum FirewallVerdict
{
    /// <summary>Not read: this is not the Windows package, or nothing is waiting on the firewall.</summary>
    NotChecked,

    /// <summary>An enabled allow rule covers the network this PC is on and no block rule does.</summary>
    Allows,

    /// <summary>No allow rule covers the network this PC is on, or a block rule does.</summary>
    Blocks,
}

/// <summary>
/// Everything System › About needs to show and change network access.
/// </summary>
/// <param name="State">The effective reach: who can connect right now.</param>
/// <param name="Scope">Who the running server listens for; null when this copy does not manage that.</param>
/// <param name="PendingScope">The saved choice when the running server has not caught up with it yet.</param>
/// <param name="Firewall">What the firewall says, read only while it matters.</param>
/// <param name="Port">The port the server listens on.</param>
/// <param name="Addresses">The addresses another device would type, when the server listens for the network.</param>
public sealed record NetworkAccessStatus(
    NetworkAccessState State,
    NetworkScope? Scope,
    NetworkScope? PendingScope,
    FirewallVerdict Firewall,
    int Port,
    IReadOnlyList<string> Addresses);

/// <summary>
/// Reads and changes whether Weir can be reached from the network, for System › About. Registered by
/// <c>WeirPlatformServices.AddWeirPlatform</c>: the Windows package works with its tray (<see cref="WindowsNetworkAccess"/>),
/// every other install reports <see cref="NetworkAccessState.NotApplicable"/> and explains who does decide.
/// </summary>
public interface INetworkAccess
{
    NetworkAccessStatus Read();

    /// <summary>Why the choice cannot be changed here, as a sentence for the operator; null when it can.</summary>
    string? NotChangeableReason { get; }

    /// <summary>
    /// Saves the choice where the tray picks it up. The running server does not change until the tray has restarted
    /// it, so <see cref="Read"/> reports the new choice as pending until then.
    /// </summary>
    /// <exception cref="InvalidOperationException">The choice is not changeable here (<see cref="NotChangeableReason"/>).</exception>
    void Choose(NetworkScope scope);
}

/// <summary>Docker and a bare install: the way the server is started decides who can reach it, so there is nothing to change here.</summary>
public sealed class UnmanagedNetworkAccess : INetworkAccess
{
    private const string DockerReason = "Set by Docker's port mapping. Change the published port in your compose file or docker run command to change who can reach Weir.";

    private const string BindReason = "Set by the bind address Weir was started with. Start it with --host 127.0.0.1 for this PC only, or --host 0.0.0.0 for devices on your network.";

    private readonly ServerRunMode _runMode;
    private readonly ServerListenOptions _listen;

    public UnmanagedNetworkAccess(ServerRunMode runMode, ServerListenOptions listen)
    {
        _runMode = runMode ?? throw new ArgumentNullException(nameof(runMode));
        _listen = listen ?? throw new ArgumentNullException(nameof(listen));
    }

    public string? NotChangeableReason => _runMode == ServerRunMode.Docker ? DockerReason : BindReason;

    public NetworkAccessStatus Read() =>
        new(NetworkAccessState.NotApplicable, Scope: null, PendingScope: null, FirewallVerdict.NotChecked, _listen.Port, Addresses: []);

    public void Choose(NetworkScope scope) =>
        throw new InvalidOperationException(NotChangeableReason);
}
