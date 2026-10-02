using Weir.Core.Json;
using Weir.Infrastructure.Runtime;

namespace Weir.Api.Endpoints;

/// <summary>
/// The wire form of <see cref="NetworkAccessStatus"/>: stable words for the page to branch on, and one plain sentence
/// saying who can reach Weir and what is happening about it.
/// </summary>
internal static class NetworkAccessStatusWire
{
    private const string ThisPcOnlyWire = "this_pc_only";
    private const string NetworkWire = "network";

    internal static readonly IReadOnlyList<string> Scopes = [ThisPcOnlyWire, NetworkWire];

    // Where this copy of Weir leaves the choice to something else, that reason is the summary.
    internal static WireObject From(NetworkAccessStatus status, string machineName, string? notChangeableReason) => new WireObject()
        .Set("state", WireState(status.State))
        .Set("summary", Summary(status, machineName, notChangeableReason))
        .Set("scope", status.Scope is { } scope ? WireScope(scope) : null)
        .Set("pending_scope", status.PendingScope is { } pending ? WireScope(pending) : null)
        .Set("firewall", WireFirewall(status.Firewall))
        .Set("port", status.Port)
        .Set("machine_name", machineName)
        .Set("addresses", new WireArray(status.Addresses.Select(address => (WireValue)WireValue.Of(address))));

    internal static NetworkScope ParseScope(string wire) => wire == NetworkWire ? NetworkScope.Network : NetworkScope.ThisPcOnly;

    internal static string WireScope(NetworkScope scope) => scope == NetworkScope.Network ? NetworkWire : ThisPcOnlyWire;

    /// <summary>What a choice means, as a sentence.</summary>
    internal static string Choice(NetworkScope scope) => scope == NetworkScope.Network
        ? "Other devices on your network can reach Weir."
        : "Only this PC can reach Weir.";

    private static string WireState(NetworkAccessState state) => state switch
    {
        NetworkAccessState.ThisPcOnly => ThisPcOnlyWire,
        NetworkAccessState.Allowed => "allowed",
        NetworkAccessState.Blocked => "blocked",
        _ => "not_applicable",
    };

    private static string WireFirewall(FirewallVerdict verdict) => verdict switch
    {
        FirewallVerdict.Allows => "allowed",
        FirewallVerdict.Blocks => "blocked",
        _ => "not_checked",
    };

    private static string Summary(NetworkAccessStatus status, string machineName, string? notChangeableReason) => (status.State, status.PendingScope) switch
    {
        (NetworkAccessState.NotApplicable, _) => notChangeableReason ?? "",
        (_, NetworkScope.ThisPcOnly) => "Restarting Weir so only this PC can reach it.",
        (_, NetworkScope.Network) when status.Firewall == FirewallVerdict.Allows =>
            "Restarting Weir so other devices on your network can reach it.",
        (_, NetworkScope.Network) =>
            $"Waiting for you to approve Windows Firewall on {machineName}. Weir restarts for your network once you do.",
        (NetworkAccessState.Allowed, _) => Choice(NetworkScope.Network),
        (NetworkAccessState.Blocked, _) =>
            "Windows Firewall is blocking other devices from reaching Weir. Try again to ask Windows to allow it, or limit Weir to this PC.",
        _ => Choice(NetworkScope.ThisPcOnly),
    };
}
