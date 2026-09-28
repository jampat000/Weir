using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Velopack;
using Weir.Tray.Firewall;

namespace Weir.Tray;

static class Program
{
    private const string MutexName = @"Local\WeirTrayHostSingleton";

    /// <summary>Where Weir keeps its data; the tray and the server both read it.</summary>
    internal const string RuntimeHomeVariable = "WEIR_HOME";

    /// <summary>System › About, where the web app checks for and installs updates.</summary>
    internal const string UpdateCheckPath = "/system?tab=about";

    /// <summary>
    /// Start without opening Weir in the browser. The starts nobody asked to see pass it: at sign-in, and after an
    /// update (#638).
    /// </summary>
    internal const string NoBrowserArgument = "--no-browser";

    /// <summary>
    /// Start with no UI at all, ever: no browser, no port dialog, no error message box, no matter what the desktop
    /// heuristic in <see cref="PortChoice.HasInteractiveDesktop"/> reports. This is what a program driving Weir
    /// unattended (an installer, a provisioning script) should pass, because a wrong "yes, there's a desktop"
    /// answer from that heuristic must never turn into a dialog nobody can see or answer (#779). A silent start
    /// that cannot get a usable port still exits with a non-zero code instead of asking.
    /// </summary>
    internal const string SilentArgument = "--silent";

    [STAThread]
    static int Main(string[] args)
    {
        // --configure-firewall, --remove-firewall and --allow-lan are Weir's own one-shot, headless commands, not
        // Velopack's install machinery — checked first, before RunInstallerHooks, so the elevated relaunch this
        // process itself starts for the first-run prompt (FirewallElevation, below) can never re-enter Velopack's
        // own first-run handling a second time. Whoever called them waits for this process to exit.
        if (FirewallCommand.Handles(args))
        {
            return FirewallCommand.Run(args, LogToConsoleAndFile);
        }

        RunInstallerHooks();

        if (args.Contains("--version"))
        {
            Console.WriteLine(typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown");
            return 0;
        }

        using var mutex = new Mutex(false, MutexName, out bool createdNew);
        if (!createdNew)
        {
            HandOverToRunningTray(args);
            return 0;
        }

        LogUnhandledErrors();
        return RunTray(args);
    }

    // Velopack runs Weir.exe with its own arguments to install, update and uninstall; these hooks run then and
    // exit, and a normal start falls through.
    private static void RunInstallerHooks() =>
        VelopackApp.Build()
            .OnAfterInstallFastCallback((v) =>
            {
                TrayLog.Write($"Velopack: after install v{v}");
                KillRunningProcesses($"Velopack after install v{v}");
                StartupRegistration.Register();
            })
            .OnBeforeUninstallFastCallback((v) =>
            {
                TrayLog.Write($"Velopack: before uninstall v{v}");
                KillRunningProcesses($"Velopack before uninstall v{v}");
                StartupRegistration.Deregister();
                RemoveFirewallRuleIfElevated();
            })
            .OnBeforeUpdateFastCallback((v) =>
            {
                TrayLog.Write($"Velopack: before update to v{v}");
            })
            .OnAfterUpdateFastCallback((v) =>
            {
                TrayLog.Write($"Velopack: after update to v{v}");
                StartupRegistration.Register();
            })
            // Unlike the FastCallback hooks above, OnFirstRun runs in-process as part of a normal app start and is
            // allowed to show UI (docs.velopack.io) — the one place Weir asks its one Windows admin (UAC) prompt
            // for LAN access, per the owner's decision. It never runs for a --silent start's first launch either,
            // because --silent skips Velopack's post-install app launch entirely (docs/release.md, #779).
            .OnFirstRun((v) => PromptForFirewallAccess())
            .Run();

    // A FastCallback hook has a 30-second budget and must show nothing (Velopack.VelopackApp docs), so this never
    // asks for elevation — it only acts when the uninstaller already happens to be running elevated. Left in place
    // otherwise, the rule is harmless: it names a program that will not exist once uninstall finishes.
    private static void RemoveFirewallRuleIfElevated()
    {
        if (!FirewallCommand.IsElevated())
        {
            TrayLog.Write("Uninstall: leaving the Weir firewall rule in place (not running elevated).");
            return;
        }
        try
        {
            WeirFirewallRule.Remove(new ComFirewallPolicy());
            TrayLog.Write("Uninstall: removed the Weir firewall rule.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Uninstall: could not remove the Weir firewall rule: {ex.Message}");
        }
    }

    // The one Windows admin (UAC) prompt Weir ever asks for on its own, and only once: FirewallPromptFile records
    // the answer, even a decline, so a person who said no is never asked again at every start. "Allow other
    // devices on your network..." in the tray menu is how they revisit it later.
    private static void PromptForFirewallAccess()
    {
        var runtimeHome = RuntimeHome();
        try
        {
            if (FirewallPromptFile.AlreadyAsked(runtimeHome))
            {
                return;
            }

            var allow = MessageBox.Show(
                "Other devices on your network, such as Deluno, need Weir allowed through Windows Firewall to reach it. Allow Weir on your network?",
                "Weir",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            var outcome = allow ? FirewallElevation.ConfigureElevated(TrayLog.Write) : FirewallElevation.Outcome.Declined;
            FirewallPromptFile.MarkAsked(runtimeHome, outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"First-run firewall prompt could not record its answer: {ex.Message}");
        }
    }

    // --configure-firewall, --remove-firewall and --allow-lan have no window to report to, so their outcome goes
    // to whatever console launched them (a script watching the exit code still wants a reason for it) as well as
    // tray-host.log, the same place every other startup decision is recorded.
    private static void LogToConsoleAndFile(string message)
    {
        TrayLog.Write(message);
        Console.Error.WriteLine(message);
    }

    // Weir is already running in this session: a second start only opens it, if the person asked for that.
    private static void HandOverToRunningTray(string[] args)
    {
        TrayLog.Write("Tray host launch skipped: an existing Weir tray instance is already running.");
        if (PortChoice.SuppliedPort(args, Environment.GetEnvironmentVariable) is { } ignored)
        {
            TrayLog.Write($"Ignoring {ignored.Source} {ignored.Text}: Weir is already running. Use \"Change port\" from its tray menu, or quit it and start it again with the new port.");
        }
        if (OpensBrowser(args))
        {
            OpenExistingInstanceBrowser();
        }
    }

    private static int RunTray(string[] args)
    {
        try
        {
            // Before anything else: whatever started Weir may have redirected our stdio through pipes
            // it is waiting to see closed. Weir runs until Quit, so holding those open would make that
            // wait never end (#779). See InheritedStdioHandles.
            InheritedStdioHandles.CloseInherited(TrayLog.Write);

            // Before any window, including the port dialog.
            ApplicationConfiguration.Initialize();

            var runtimeHome = RuntimeHome();
            if (!SecureRuntimeHome(args, runtimeHome))
            {
                return 1;
            }
            var port = ResolvePort(args, runtimeHome);
            if (port is null)
            {
                return 1;
            }

            using var app = new TrayApp(port.Value, openBrowserOnReady: OpensBrowser(args));
            return app.Run();
        }
        catch (Exception ex)
        {
            // The last boundary before the process ends. The tray has no console, so whatever start-up threw is
            // logged here or not at all.
            TrayLog.Write($"Fatal startup error:\n{ex}");
            return 1;
        }
    }

    /// <summary>
    /// A tray icon has no window to show an error in, so a failure in a menu handler or a background loop is
    /// written to tray-host.log and the tray keeps running, instead of WinForms' crash dialog or a silent exit.
    /// </summary>
    private static void LogUnhandledErrors()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => TrayLog.Write($"Unhandled error on the tray's UI thread:\n{e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => TrayLog.Write($"Unhandled error; the tray is stopping:\n{e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TrayLog.Write($"Unobserved background error:\n{e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>
    /// Creates the runtime home owner-only, or tightens it (RuntimeHomeSecurity). Returns false when Weir must not
    /// start because another account owns the folder; the person at the desktop, if any, is told why, unless
    /// <see cref="SilentArgument"/> rules out showing anything.
    /// </summary>
    private static bool SecureRuntimeHome(string[] args, string runtimeHome)
    {
        try
        {
            if (RuntimeHomeSecurity.Secure(runtimeHome))
            {
                TrayLog.Write($"Restricted {runtimeHome} to SYSTEM, Administrators and {Environment.UserName}.");
            }
            return true;
        }
        catch (RuntimeHomeOwnedByAnotherAccountException ex)
        {
            TrayLog.Write($"Not starting: {ex.Message}");
            if (HasInteractiveDesktop(args, PortChoice.HasInteractiveDesktop))
            {
                MessageBox.Show(ex.Message, "Weir", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            // Weir still starts: its data works, it is only readable by more accounts than it should be.
            TrayLog.Write($"Could not restrict who can read {runtimeHome} ({ex.Message}); it keeps its current access list.");
            return true;
        }
    }

    /// <summary>
    /// The port to run on: supplied on the command line or in WEIR_PORT, else saved by an
    /// earlier run, else chosen now — by the person if there is one, by the default if not.
    /// Null means do not start; the reason is in tray-host.log.
    /// </summary>
    private static int? ResolvePort(string[] args, string runtimeHome)
    {
        var supplied = PortChoice.SuppliedPort(args, Environment.GetEnvironmentVariable);
        var saved = PortChoice.LoadSaved(runtimeHome);
        var interactive = HasInteractiveDesktop(args, PortChoice.HasInteractiveDesktop);

        // A server left behind by a tray that crashed still holds our port, and would make
        // the saved port look taken by "another program". This tray holds the session's
        // singleton mutex, so any WeirServer.exe from this install in this session is an orphan.
        StopOrphanedServers();

        var decision = PortChoice.Decide(
            supplied,
            saved,
            interactive,
            PortChoice.IsInUse,
            prompt => PortDialog.Ask(prompt, PortChoice.IsInUse, LoadAppIcon()));

        TrayLog.Write($"Port: {decision.Reason} (interactive desktop: {(interactive ? "yes" : "no")})");
        if (decision.Port is { } port && decision.Save)
        {
            PortChoice.Save(runtimeHome, port);
            TrayLog.Write($"Saved port {port} to {Path.Combine(runtimeHome, PortChoice.SavedPortFileName)}.");
        }
        return decision.Port;
    }

    private static void StopOrphanedServers() =>
        InstallProcesses.StopOwn(InstallProcesses.Root(), sameSessionOnly: true, TrayLog.Write, "Startup (orphaned server check)");

    internal static Icon LoadAppIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("weir-tray-icon.ico", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            return new Icon(stream);
        }

        var fileCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "weir-tray-icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "assets", "weir-tray-icon.ico"),
        };
        foreach (var path in fileCandidates)
        {
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }

        return SystemIcons.Application;
    }

    // A second launch while Weir is running: open the running one, at the port its server is listening on.
    private static void OpenExistingInstanceBrowser()
    {
        var portFile = Path.Combine(RuntimeHome(), ServerHost.CurrentPortFileName);
        try
        {
            if (!File.Exists(portFile))
            {
                return;
            }
            var text = File.ReadAllText(portFile).Trim();
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535)
            {
                OpenBrowser(port);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not read {portFile} to open the running Weir: {ex.Message}");
        }
    }

    // Velopack's install and uninstall hooks: stop this install's own tray and server so their
    // files can be replaced or removed. Only this install's — see InstallProcesses.
    private static void KillRunningProcesses(string why) =>
        InstallProcesses.StopOwn(InstallProcesses.Root(), sameSessionOnly: false, TrayLog.Write, why);

    internal static string RuntimeHome()
    {
        var env = Environment.GetEnvironmentVariable(RuntimeHomeVariable)?.Trim();
        if (!string.IsNullOrEmpty(env))
        {
            return Path.GetFullPath(env);
        }
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(programData))
        {
            programData = @"C:\ProgramData";
        }
        return Path.Combine(programData, "Weir");
    }

    /// <summary>Whether a start with these arguments opens Weir in the browser once its server is healthy.</summary>
    internal static bool OpensBrowser(IEnumerable<string> args) => !args.Contains(NoBrowserArgument) && !IsSilent(args);

    /// <summary>Whether <see cref="SilentArgument"/> was passed: no UI of any kind, ever, for this start.</summary>
    internal static bool IsSilent(IEnumerable<string> args) => args.Contains(SilentArgument);

    /// <summary>
    /// Whether there is a person to show UI to: <paramref name="desktopCheck"/>, unless <see cref="SilentArgument"/>
    /// overrides it. Silent always wins, because a start driven by another program must never gamble a hang on
    /// that heuristic being right. The check is passed in (as <see cref="PortChoice.Decide"/> does with its own
    /// side effects) so the override is testable without a real desktop.
    /// </summary>
    internal static bool HasInteractiveDesktop(IEnumerable<string> args, Func<bool> desktopCheck) => !IsSilent(args) && desktopCheck();

    internal static bool OpenBrowser(
        int port,
        Action<ProcessStartInfo>? startProcess = null,
        string relativePath = "/")
    {
        if (!relativePath.StartsWith('/') || relativePath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Browser path must be local to Weir.", nameof(relativePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = $"http://127.0.0.1:{port}{relativePath}",
            UseShellExecute = true,
        };

        try
        {
            (startProcess ?? (info => Process.Start(info)?.Dispose()))(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Opening a browser is a convenience action. Session 0, disconnected RDP
            // sessions, and hardened shell policies can reject shell execution; none of
            // those conditions should stop the tray watchdog or its server process.
            TrayLog.Write($"Could not open Weir in the browser: {ex.Message}");
            return false;
        }
    }
}
