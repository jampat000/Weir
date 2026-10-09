using Weir.Tray.Firewall;

namespace Weir.Tray.LanAccess;

/// <summary>What the tray tells the person after a LAN access change, in plain words.</summary>
sealed record LanAccessNotice(string Text, bool IsWarning, bool NamesLog = false)
{
    private const string SeeLog = "See tray-host.log in the data folder.";

    /// <summary>The server was asked to listen for <paramref name="target"/>, from the menu or from <c>--allow-lan</c>.</summary>
    internal static LanAccessNotice Restarted(ListenScope target, ScopeChange change) => (target, change) switch
    {
        (ListenScope.OtherDevices, ScopeChange.Applied) =>
            new("Other devices on your network can now reach Weir. Weir restarted to apply this.", IsWarning: false),
        (ListenScope.OtherDevices, ScopeChange.Failed) =>
            new($"Weir could not restart for other devices, so only this PC can reach it. {SeeLog}", IsWarning: true, NamesLog: true),
        (ListenScope.ThisPcOnly, ScopeChange.Applied) =>
            new("Only this PC can reach Weir now. Weir restarted to apply this.", IsWarning: false),
        (ListenScope.ThisPcOnly, ScopeChange.Failed) =>
            new($"Weir could not restart to limit itself to this PC, so nothing changed. {SeeLog}", IsWarning: true, NamesLog: true),
        (ListenScope.OtherDevices, _) => new("Other devices on your network can already reach Weir.", IsWarning: false),
        _ => new("Only this PC could already reach Weir.", IsWarning: false),
    };

    /// <summary>A choice another process saved was carried out, and how the Windows admin prompt for it was answered.</summary>
    internal static LanAccessNotice ForSavedChoice(SavedChoiceApplied applied) => applied switch
    {
        { Change: ScopeChange.Failed } => Restarted(applied.Scope, applied.Change),
        { Firewall: FirewallElevation.Outcome.Declined, Change: ScopeChange.Applied } =>
            new("Weir now listens for other devices, but Windows admin access was not granted, so Windows Firewall still blocks them. You can try again from System, then About.", IsWarning: true),
        { Firewall: FirewallElevation.Outcome.Failed, Change: ScopeChange.Applied } =>
            new($"Weir now listens for other devices, but Windows Firewall was not changed, so it still blocks them. {SeeLog}", IsWarning: true, NamesLog: true),
        { Firewall: FirewallElevation.Outcome.Declined or FirewallElevation.Outcome.Failed } =>
            FirewallStepFailed(applied.Firewall.Value, applied.Scope),
        { Firewall: FirewallElevation.Outcome.Configured, Change: ScopeChange.Unchanged } => FirewallAllowsWeir(),
        _ => Restarted(applied.Scope, applied.Change),
    };

    /// <summary>The Windows admin step of "Allow other devices on your network..." did not create the rule.</summary>
    internal static LanAccessNotice FirewallStepFailed(FirewallElevation.Outcome outcome, ListenScope current) => outcome switch
    {
        FirewallElevation.Outcome.Declined when current == ListenScope.ThisPcOnly =>
            new("Windows admin access was not granted, so other devices still cannot reach Weir.", IsWarning: true),
        FirewallElevation.Outcome.Declined =>
            new("Windows admin access was not granted, so Windows Firewall was not changed.", IsWarning: true),
        _ => new($"Could not allow other devices on your network. {SeeLog}", IsWarning: true, NamesLog: true),
    };

    /// <summary>The choice could not be written to the data folder, so the server was not changed.</summary>
    internal static LanAccessNotice NotSaved() =>
        new($"Weir could not save the network choice, so Weir is still set up the way it was. {SeeLog}", IsWarning: true, NamesLog: true);

    /// <summary>Windows Firewall now allows Weir, and the server was already listening for other devices.</summary>
    internal static LanAccessNotice FirewallAllowsWeir() =>
        new("Windows Firewall now allows other devices on your network to reach Weir.", IsWarning: false);
}
