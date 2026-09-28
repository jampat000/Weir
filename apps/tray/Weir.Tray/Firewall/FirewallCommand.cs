using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Weir.Tray.Firewall;

/// <summary>
/// The command-line entry points for Weir's firewall rule: <c>--configure-firewall</c> and
/// <c>--remove-firewall</c> (run already elevated — <see cref="FirewallElevation"/> is what gets a process there,
/// through Windows' own UAC prompt) and <c>--allow-lan</c> (the documented flag for a program driving Weir
/// unattended, #779: it never elevates itself and never prompts, it only checks). None of these ever show a
/// window, so <c>--silent</c> changes nothing about them; they are headless by construction.
/// </summary>
static class FirewallCommand
{
    internal const string ConfigureArgument = "--configure-firewall";
    internal const string RemoveArgument = "--remove-firewall";
    internal const string AllowLanArgument = "--allow-lan";

    internal static bool Handles(IEnumerable<string> args) =>
        args.Contains(ConfigureArgument) || args.Contains(RemoveArgument) || args.Contains(AllowLanArgument);

    /// <summary>Runs whichever firewall argument is present. <see cref="Handles"/> must be true first.</summary>
    internal static int Run(IReadOnlyList<string> args, Action<string> log) =>
        Run(args, log, IsElevated, () => new ComFirewallPolicy());

    /// <summary>The same behaviour, with elevation checking and the firewall policy itself substitutable for tests.</summary>
    internal static int Run(IReadOnlyList<string> args, Action<string> log, Func<bool> isElevated, Func<IFirewallPolicy> openPolicy)
    {
        if (args.Contains(RemoveArgument))
        {
            return RunElevated(log, isElevated, openPolicy, "remove", policy => WeirFirewallRule.Remove(policy));
        }
        if (args.Contains(ConfigureArgument) || args.Contains(AllowLanArgument))
        {
            return RunElevated(log, isElevated, openPolicy, "configure", policy => LogConfigureResult(policy, log));
        }
        throw new InvalidOperationException($"{nameof(FirewallCommand)}.{nameof(Run)} was called without a firewall argument.");
    }

    private static int RunElevated(Action<string> log, Func<bool> isElevated, Func<IFirewallPolicy> openPolicy, string verb, Action<IFirewallPolicy> apply)
    {
        if (!isElevated())
        {
            log($"Cannot {verb} the Weir firewall rule: this process is not running as administrator.");
            return FirewallExitCode.NotElevated;
        }

        try
        {
            apply(openPolicy());
            return FirewallExitCode.Success;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            log($"Could not {verb} the Weir firewall rule: {ex.Message}");
            return FirewallExitCode.FirewallApiError;
        }
    }

    private static void LogConfigureResult(IFirewallPolicy policy, Action<string> log)
    {
        var summary = WeirFirewallRule.Configure(policy, InstallProcesses.Root());
        log($"Firewall rule '{WeirFirewallRule.RuleName}' is in place (Private, Domain profiles). Removed {summary.BlockRulesRemoved} block rule(s) for Weir's server.");
    }

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
