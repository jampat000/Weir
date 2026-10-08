using System.ComponentModel;
using System.Runtime.InteropServices;
using Weir.Tray.Firewall;
using Weir.Tray.LanAccess;

namespace Weir.Tray;

/// <summary>How this run was started.</summary>
/// <param name="OpenBrowserOnReady">Whether Weir opens in the browser once its server is ready.</param>
/// <param name="Interactive">Whether there is a person to tell when the server cannot start. A start with no one to tell exits instead.</param>
readonly record struct TrayStart(bool OpenBrowserOnReady, bool Interactive);

/// <summary>
/// The tray icon and its menu. Everything that touches them runs on the UI thread: work on other threads (the
/// server watchdog, update checks, a port change) hands its result back through <see cref="OnUi"/>. The icon, its hover
/// text and the menu are all drawn from one place, <see cref="Render"/>, from the state the tray keeps, so they cannot
/// disagree.
/// </summary>
sealed class TrayApp : IDisposable
{
    private static readonly TimeSpan BrowserDebounceWindow = TimeSpan.FromMilliseconds(1250);

    private readonly string _runtimeHome;
    private readonly TrayStart _start;
    private readonly ServerHost _server;
    private readonly LanAccessSync _lanAccess;
    private readonly UpdateSettings _updateSettings;
    private readonly IUpdateService _updateService;
    private readonly TrayShutdown _shutdown;
    private readonly StartupRegistration _startup = StartupRegistration.ForThisUser();
    private readonly Debounce _browserDebounce = new(BrowserDebounceWindow, TimeProvider.System);
    private readonly CancellationTokenSource _cts = new();

    private SynchronizationContext? _ui;
    private NotifyIcon? _notifyIcon;
    private TrayIcons? _icons;
    private TrayMenuView? _menu;
    private TrayStatusWatcher? _statusWatcher;
    private SecondLaunchSignal? _secondLaunch;
    private TrayUpdates? _updates;
    private LanAccessMenu? _lanAccessMenu;
    private TrayStatus? _serverStatus;
    private int? _movingToPort;
    private bool _watching;
    private Action? _balloonClick;
    private int _exitCode;

    public TrayApp(int port, ListenScope listenScope, TrayStart start, IUpdateService updateService, UpdateSettings updateSettings)
    {
        var installRoot = AppContext.BaseDirectory;
        _runtimeHome = Program.RuntimeHome();
        _start = start;
        _server = new ServerHost(_runtimeHome, installRoot, port, listenScope);
        _lanAccess = new LanAccessSync(_runtimeHome, _server, new WindowsFirewallAccess(), TimeProvider.System);
        _updateSettings = updateSettings;
        _updateService = updateService;
        _shutdown = new TrayShutdown(StopServerAsync, updateService);

        TrayLog.Write($"Starting tray host. installRoot={installRoot} runtimeHome={_runtimeHome}");
    }

    /// <summary>Starts the server and runs the tray until Quit or an update restart. Returns the process exit code.</summary>
    public int Run()
    {
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
        _ui = SynchronizationContext.Current;

        _server.PrepareEnvironment();
        _server.WritePortFile();
        TrayLog.Write($"Prepared runtime environment on port {_server.Port}");

        // The icon is there from the first moment, marked Starting, not only once the server answers.
        CreateTrayIcon();
        _server.PhaseChanged += () => OnUi(Render);
        _statusWatcher = new TrayStatusWatcher(_runtimeHome, status => OnUi(() => ShowServerStatus(status)));
        _statusWatcher.Start();
        _secondLaunch = new SecondLaunchSignal(() => OnUi(ShowAlreadyRunning));

        // Start-up runs inside the message loop, so waiting for the server never blocks the UI thread.
        OnUi(() => BackgroundWork.Observe("Start-up", StartAsync()));
        TrayLog.Write("Starting tray icon event loop");
        Application.Run();
        TrayLog.Write("Tray icon event loop ended.");
        return _exitCode;
    }

    private async Task StartAsync()
    {
        TrayLog.Write("Waiting for local health endpoint");
        try
        {
            await _server.StartAsync(_cts.Token);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or FileNotFoundException or Win32Exception)
        {
            TrayLog.Write($"Fatal startup error:\n{ex}");
            if (!_start.Interactive)
            {
                _exitCode = 1;
                Application.ExitThread();
                return;
            }

            // The person is there: say so, and keep the icon, whose Restart Weir is the way back.
            ShowBalloon("Weir", TrayBalloons.CouldNotStartText, ToolTipIcon.Error, RestartObserved);
            return;
        }
        TrayLog.Write($"Weir is healthy on http://127.0.0.1:{_server.Port}/");

        if (_start.OpenBrowserOnReady)
        {
            OpenBrowserDebounced("startup");
        }
        else
        {
            TrayLog.Write("Skipping browser auto-open (no-browser mode).");
        }

        BeginWatching();
    }

    // Once the server has been up: watch it, follow the LAN access choice and look for updates. Run once, whichever
    // start, first or Restart Weir after a failed one, gets the server up.
    private void BeginWatching()
    {
        if (_watching)
        {
            return;
        }
        _watching = true;

        _server.Watch(() => OnUi(() => ShowBalloon("Weir", TrayBalloons.StoppedText, ToolTipIcon.Error, RestartObserved)), _cts.Token);

        _ = BackgroundWork.RunLoop(
            "LAN access watcher",
            ct => _lanAccess.WatchAsync(
                new LanAccessWatch(
                    () => OnUi(() => _lanAccessMenu!.OnWaitingForWindows()),
                    applied => OnUi(() => _lanAccessMenu!.OnChangedElsewhere(applied))),
                ct),
            _cts.Token);

        _updates!.Start();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread, after whatever it is doing at the moment.</summary>
    private void OnUi(Action action) => _ui!.Post(_ => action(), null);

    // -- What the tray shows ------------------------------------------------

    private TrayState State() =>
        new(_server.Phase, _serverStatus, _updates is { IsDownloaded: true } updates ? updates.PendingVersion : null, _server.Port);

    // The icon's mark, the hover text and the menu, from the state as it is now.
    private void Render()
    {
        if (_notifyIcon is null || _menu is null || _updates is null || _lanAccessMenu is null)
        {
            return;
        }
        var state = State();
        _notifyIcon.Icon = _icons!.For(state.Badge);
        _notifyIcon.Text = state.HoverText;
        _menu.Show(TrayMenu.Describe(new TrayMenuInputs(
            state,
            _updates.MenuState(),
            _lanAccessMenu.State,
            _movingToPort,
            _startup.IsEnabled,
            AppVersion.Current)));
    }

    private void ShowServerStatus(TrayStatus? status)
    {
        _serverStatus = status;
        Render();
    }

    // -- Updates ------------------------------------------------------------

    private void ShowUpdateNotice(UpdateMode mode)
    {
        var version = _updates?.PendingVersion ?? "new version";
        switch (mode)
        {
            case UpdateMode.Auto:
                TrayLog.Write($"Notifying user: update v{version} downloaded; it installs once Weir has been idle for {IdleInstall.IdlePeriodText}, or when Weir next quits or starts.");
                ShowBalloon(
                    "Weir Update Ready",
                    $"Version {version} has been downloaded. Weir installs it and restarts by itself once it has been idle for {IdleInstall.IdlePeriodText}, or when Weir next quits or starts.",
                    ToolTipIcon.Info);
                break;
            case UpdateMode.DownloadOnly:
                TrayLog.Write($"Notifying user: update v{version} downloaded and ready to install.");
                ShowBalloon(
                    "Weir Update Ready",
                    $"Version {version} has been downloaded. Click here to restart and install it, or it installs the next time Weir quits or starts.",
                    ToolTipIcon.Info,
                    () =>
                    {
                        TrayLog.Write("Restart to update chosen from the update notice.");
                        BackgroundWork.Observe("Apply update", ApplyUpdateAndRestartAsync());
                    });
                break;
            default:
                TrayLog.Write($"Notifying user: update v{version} available.");
                ShowBalloon(
                    "Weir Update",
                    $"Version {version} is available. Open System › About in Weir to update.",
                    ToolTipIcon.Info,
                    () => OpenBrowserDebounced("tray-update-balloon", Program.UpdateCheckPath));
                break;
        }
        Render();
    }

    private void OnUpdateMenuClick()
    {
        switch (_updates?.MenuState().Action)
        {
            case UpdateMenuAction.OpenUpdatePage:
                OpenBrowserDebounced("tray-update-settings", Program.UpdateCheckPath);
                break;
            case UpdateMenuAction.Check:
                _updates.CheckInBackground();
                break;
            case UpdateMenuAction.Download:
                _updates.DownloadInBackground();
                break;
            case UpdateMenuAction.Restart:
                TrayLog.Write("Restart to update chosen from the tray menu.");
                BackgroundWork.Observe("Apply update", ApplyUpdateAndRestartAsync());
                break;
            default:
                break;
        }
    }

    private async Task ApplyUpdateAndRestartAsync()
    {
        if (_updates is not { IsDownloaded: true })
        {
            return;
        }
        TrayLog.Write("Applying the downloaded update and restarting.");
        await _shutdown.RestartToUpdateAsync();
    }

    // The server is stopped through ServerProcessStop before anything else happens, so nothing that follows, an
    // update install included, can cut a job short (#857).
    private async Task StopServerAsync()
    {
        await _cts.CancelAsync();
        await _server.StopAsync();
        _notifyIcon!.Visible = false;
    }

    // -- Tray icon ----------------------------------------------------------

    private void CreateTrayIcon()
    {
        _lanAccessMenu = new LanAccessMenu(_lanAccess, _server, ShowLanAccessNotice, Render, _cts.Token);
        _updates = new TrayUpdates(
            _updateService,
            _runtimeHome,
            _updateSettings,
            new UpdateCallbacks(
                OnUi,
                Render,
                ShowUpdateNotice,
                () => BackgroundWork.Observe("Apply update", ApplyUpdateAndRestartAsync())),
            TimeProvider.System,
            _cts.Token);
        _icons = new TrayIcons(size => Program.LoadAppIcon(size), SystemInformation.SmallIconSize);
        _menu = new TrayMenuView(MenuClicks(), staysOpen: [TrayMenuItem.StartWithWindows]);
        _menu.Strip.Opening += (_, _) => Render();

        _notifyIcon = new NotifyIcon { ContextMenuStrip = _menu.Strip };

        // Clicking the icon is a person asking for Weir, so one click opens it (#638). A double-click still opens
        // one window: its second event falls inside the debounce.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenWeir("tray-click");
            }
        };
        _notifyIcon.DoubleClick += (_, _) => OpenWeir("tray-dblclick");

        // One handler for every balloon: a click does what the balloon on screen offered, and nothing for a balloon
        // that offered nothing.
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var onClick = _balloonClick;
            _balloonClick = null;
            onClick?.Invoke();
        };

        Render();
        _notifyIcon.Visible = true;
    }

    private Dictionary<TrayMenuItem, Action> MenuClicks() => new()
    {
        [TrayMenuItem.Open] = () => OpenWeir("tray"),
        [TrayMenuItem.Pause] = TogglePause,
        [TrayMenuItem.Restart] = RestartObserved,
        [TrayMenuItem.CopyAddress] = CopyAddress,
        [TrayMenuItem.AllowOtherDevices] = () => _lanAccessMenu!.Allow(),
        [TrayMenuItem.OnlyThisPc] = () => _lanAccessMenu!.LimitToThisPc(),
        [TrayMenuItem.ChangePort] = () => BackgroundWork.Observe("Change port", ChangePortAsync()),
        [TrayMenuItem.OpenDataFolder] = () => RuntimeFolders.Open(_runtimeHome, "data folder"),
        [TrayMenuItem.OpenLogsFolder] = OpenLogsFolder,
        [TrayMenuItem.StartWithWindows] = ToggleStartWithWindows,
        [TrayMenuItem.Update] = OnUpdateMenuClick,
        [TrayMenuItem.ReportProblem] = () => ProblemReport.Open(LogsFolder()),
        [TrayMenuItem.Quit] = () => BackgroundWork.Observe("Quit", QuitAsync()),
    };

    private void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        if (_notifyIcon is null)
        {
            return;
        }
        _balloonClick = onClick;
        _notifyIcon.ShowBalloonTip(TrayBalloons.TimeoutMs(icon), title, text, icon);
    }

    private string LogsFolder() => RuntimeFolders.Logs(_runtimeHome, Environment.GetEnvironmentVariable);

    private void OpenLogsFolder() => RuntimeFolders.Open(LogsFolder(), "logs folder");

    private async Task QuitAsync()
    {
        TrayLog.Write("Quit requested from tray icon");
        await _shutdown.QuitAsync();
        Application.Exit();
    }

    // -- Restart, pause, address, start with Windows --------------------------

    private void RestartObserved() => BackgroundWork.Observe("Restart Weir", RestartAsync());

    private async Task RestartAsync()
    {
        TrayLog.Write("Restart Weir chosen.");
        try
        {
            if (await _server.RestartNowAsync(_cts.Token))
            {
                BeginWatching();
            }
            else
            {
                ShowBalloon("Weir", TrayBalloons.CouldNotStartText, ToolTipIcon.Error, RestartObserved);
            }
        }
        catch (OperationCanceledException)
        {
            TrayLog.Write("Restart Weir: stopped, because Weir is quitting or updating.");
        }
    }

    // Asks the server to pause or resume; it answers by rewriting tray-status.json, which is what the menu then shows.
    private void TogglePause()
    {
        var pause = !State().IsPaused;
        try
        {
            PauseRequestFile.Write(_runtimeHome, pause, TimeProvider.System.GetUtcNow());
            TrayLog.Write($"{(pause ? "Pause" : "Resume")} processing asked for from the tray menu.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not ask the server to {(pause ? "pause" : "resume")}: {ex.Message}");
            ShowBalloon(
                "Weir",
                $"Weir couldn't {(pause ? "pause" : "resume")} processing. See tray-host.log in the data folder.",
                ToolTipIcon.Warning,
                () => RuntimeFolders.Open(_runtimeHome, "data folder"));
        }
    }

    private void CopyAddress()
    {
        var address = TrayState.AddressToCopy(_server.Scope, _server.Port, Environment.MachineName);
        try
        {
            Clipboard.SetText(address);
            TrayLog.Write($"Copied {address} to the clipboard.");
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            TrayLog.Write($"Could not copy {address} to the clipboard: {ex.Message}");
        }
    }

    private void ToggleStartWithWindows()
    {
        if (_startup.IsEnabled)
        {
            _startup.Disable();
        }
        else
        {
            _startup.Enable();
        }
        Render();
    }

    // -- Clicks and balloons about opening ------------------------------------

    // A click on the icon or Open Weir: opens Weir, or says why it cannot yet.
    private void OpenWeir(string source)
    {
        switch (_server.Phase)
        {
            case ServerPhase.Running:
                OpenBrowserDebounced(source);
                break;
            case ServerPhase.Starting:
                ShowBalloon("Weir", TrayBalloons.StillStartingText, ToolTipIcon.Info);
                break;
            default:
                ShowBalloon("Weir", TrayBalloons.NotRunningText, ToolTipIcon.Warning, RestartObserved);
                break;
        }
    }

    // A second launch signalled: this copy is the one running.
    private void ShowAlreadyRunning()
    {
        TrayLog.Write("Weir was started again; telling the person it is already running.");
        ShowBalloon("Weir", TrayBalloons.AlreadyRunningText(_server.Port), ToolTipIcon.Info);
    }

    // -- LAN access ---------------------------------------------------------

    private void ShowLanAccessNotice(LanAccessNotice notice) =>
        ShowBalloon(
            "Weir",
            notice.Text,
            notice.IsWarning ? ToolTipIcon.Warning : ToolTipIcon.Info,
            notice.NamesLog ? () => RuntimeFolders.Open(_runtimeHome, "data folder") : null);

    // -- Port -------------------------------------------------------------

    private async Task ChangePortAsync()
    {
        var current = _server.Port;
        var chosen = PortDialog.Ask(
            new PortPrompt(PortPromptReason.Change, current, CurrentPortInUse: false, Suggested: current),
            PortChoice.IsInUse,
            _notifyIcon?.Icon);
        if (chosen is not { } port || port == current)
        {
            TrayLog.Write("Change port: closed without a new port.");
            return;
        }

        _movingToPort = port;
        Render();
        try
        {
            // Restarting waits up to a minute for the new server; the menu stays responsive meanwhile, and this
            // method carries on here, on the UI thread, when it is done.
            if (await _server.MoveToPortAsync(port, _cts.Token))
            {
                // Moving the port is not asking to open Weir: the balloon offers the new address, and a click opens it.
                ShowBalloon("Weir", TrayBalloons.PortMovedText(port), ToolTipIcon.Info, () => OpenBrowserDebounced("port-change-balloon"));
            }
            else
            {
                ShowBalloon(
                    "Weir",
                    $"Weir could not start on port {port}, so it is still at port {current}. See tray-host.log in the data folder.",
                    ToolTipIcon.Warning,
                    () => RuntimeFolders.Open(_runtimeHome, "data folder"));
            }
        }
        catch (OperationCanceledException)
        {
            TrayLog.Write($"Change port: stopped before port {port} was ready, because Weir is quitting or updating.");
        }
        finally
        {
            _movingToPort = null;
            Render();
        }
    }

    // -- Browser ------------------------------------------------------------

    private void OpenBrowserDebounced(string source, string relativePath = "/")
    {
        if (!_browserDebounce.Allow())
        {
            TrayLog.Write($"Ignoring duplicate browser open request within debounce window (source={source}).");
            return;
        }
        TrayLog.Write($"Opening Weir in browser on port {_server.Port} (source={source})");
        Program.OpenBrowser(_server.Port, relativePath: relativePath);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _statusWatcher?.Dispose();
        _secondLaunch?.Dispose();
        _server.Dispose();
        _notifyIcon?.Dispose();
        _menu?.Dispose();
        _icons?.Dispose();
        _lanAccess.Dispose();
        _cts.Dispose();
    }
}
