using System.ComponentModel;
using System.Diagnostics;

namespace Weir.Tray;

/// <summary>
/// The tray icon and its menu. Everything that touches them runs on the UI thread: work on other threads (the
/// server watchdog, update checks, a port change) hands its result back through <see cref="OnUi"/>.
/// </summary>
sealed class TrayApp : IDisposable
{
    private const int BalloonTimeoutMs = 8000;
    private const double BrowserDebounceMs = 1250;

    private readonly string _runtimeHome;
    private readonly bool _openBrowserOnReady;
    private readonly ServerHost _server;
    private readonly UpdateSettings _updateSettings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _browserLock = new();

    private SynchronizationContext? _ui;
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _updateMenuItem;
    private ToolStripMenuItem? _portMenuItem;
    private TrayUpdates? _updates;
    private long _lastBrowserOpenTicks = long.MinValue / 2;
    private Action? _balloonClick;
    private int _exitCode;

    public TrayApp(int port, bool openBrowserOnReady)
    {
        var installRoot = AppContext.BaseDirectory;
        _runtimeHome = Program.RuntimeHome();
        _openBrowserOnReady = openBrowserOnReady;
        _server = new ServerHost(_runtimeHome, installRoot, port);
        _updateSettings = UpdateSettings.Load(_runtimeHome);

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
            _exitCode = 1;
            Application.ExitThread();
            return;
        }
        TrayLog.Write($"Weir is healthy on http://127.0.0.1:{_server.Port}/");

        if (_openBrowserOnReady)
        {
            OpenBrowserDebounced("startup");
        }
        else
        {
            TrayLog.Write("Skipping browser auto-open (no-browser mode).");
        }

        _server.Watch(() => OnUi(() => ShowBalloon("Weir", "Weir server failed repeatedly. Please restart Weir.", ToolTipIcon.Error)), _cts.Token);

        _updates = new TrayUpdates(
            _runtimeHome,
            _updateSettings,
            OnUi,
            RefreshUpdateMenu,
            ShowUpdateNotice,
            () => BackgroundWork.Observe("Apply update", ApplyUpdateAndRestartAsync()));
        _notifyIcon = CreateNotifyIcon();
        _notifyIcon.Visible = true;
        _updates.Start(_cts.Token);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread, after whatever it is doing at the moment.</summary>
    private void OnUi(Action action) => _ui!.Post(_ => action(), null);

    // -- Updates ------------------------------------------------------------

    private void ShowUpdateNotice(UpdateMode mode)
    {
        var version = _updates?.PendingVersion ?? "new version";
        switch (mode)
        {
            case UpdateMode.Auto:
                TrayLog.Write($"Notifying user: update v{version} will apply on next restart.");
                ShowBalloon("Weir Update Ready", $"Version {version} has been downloaded and will be applied automatically on next restart.", ToolTipIcon.Info);
                break;
            case UpdateMode.DownloadOnly:
                TrayLog.Write($"Notifying user: update v{version} downloaded and ready to install.");
                ShowBalloon(
                    "Weir Update Ready",
                    $"Version {version} has been downloaded. Click here to restart and update.",
                    ToolTipIcon.Info,
                    () => BackgroundWork.Observe("Apply update", ApplyUpdateAndRestartAsync()));
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
        RefreshUpdateMenu();
    }

    private void RefreshUpdateMenu()
    {
        if (_updateMenuItem is null || _updates is null)
        {
            return;
        }
        var state = _updates.MenuState();
        _updateMenuItem.Text = state.Text;
        _updateMenuItem.Enabled = state.Enabled;
    }

    private void OnUpdateMenuClick(object? sender, EventArgs e)
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
        TrayLog.Write("User requested update apply and restart.");
        await _cts.CancelAsync();
        await _server.StopAsync();
        _notifyIcon!.Visible = false;
        _updates.ApplyAndRestart();
    }

    // -- Tray icon ----------------------------------------------------------

    private NotifyIcon CreateNotifyIcon()
    {
        var menu = new ContextMenuStrip();

        var openItem = menu.Items.Add("Open Weir");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => OpenBrowserDebounced("tray");

        menu.Items.Add("Open Data Folder").Click += (_, _) => OpenDataFolder();

        _portMenuItem = new ToolStripMenuItem(PortMenuText());
        _portMenuItem.Click += (_, _) => BackgroundWork.Observe("Change port", ChangePortAsync());
        menu.Items.Add(_portMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        _updateMenuItem = new ToolStripMenuItem();
        _updateMenuItem.Click += OnUpdateMenuClick;
        RefreshUpdateMenu();
        menu.Items.Add(_updateMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Quit").Click += (_, _) => BackgroundWork.Observe("Quit", QuitAsync());

        var notifyIcon = new NotifyIcon
        {
            Icon = Program.LoadAppIcon(),
            Text = "Weir",
            ContextMenuStrip = menu,
            Visible = false,
        };

        // Clicking the icon is a person asking for Weir, so one click opens it (#638). A double-click still opens
        // one window: its second event falls inside the debounce.
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenBrowserDebounced("tray-click");
            }
        };
        notifyIcon.DoubleClick += (_, _) => OpenBrowserDebounced("tray-dblclick");

        // One handler for every balloon: a click does what the balloon on screen offered, and nothing for a balloon
        // that offered nothing.
        notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var onClick = _balloonClick;
            _balloonClick = null;
            onClick?.Invoke();
        };

        return notifyIcon;
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        if (_notifyIcon is null)
        {
            return;
        }
        _balloonClick = onClick;
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _runtimeHome, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            TrayLog.Write($"Could not open the data folder {_runtimeHome}: {ex.Message}");
            MessageBox.Show(
                $"Weir could not open its data folder. You can open it in File Explorer yourself:\n\n{_runtimeHome}",
                "Weir",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task QuitAsync()
    {
        TrayLog.Write("Quit requested from tray icon");
        if (_updates is { IsDownloaded: true })
        {
            _updates.ApplyOnExit();
            TrayLog.Write("Update will be applied after exit.");
        }
        await _cts.CancelAsync();
        await _server.StopAsync();
        _notifyIcon!.Visible = false;
        Application.Exit();
    }

    // -- Port -------------------------------------------------------------

    private string PortMenuText() => $"Change port ({_server.Port})...";

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

        _portMenuItem!.Enabled = false;
        _portMenuItem.Text = $"Moving to port {port}...";
        try
        {
            // Restarting waits up to a minute for the new server; the menu stays responsive meanwhile, and this
            // method carries on here, on the UI thread, when it is done.
            if (await _server.MoveToPortAsync(port, _cts.Token))
            {
                ShowBalloon("Weir", $"Weir is now at http://localhost:{port}/", ToolTipIcon.Info);
                OpenBrowserDebounced("port-change");
            }
            else
            {
                ShowBalloon(
                    "Weir",
                    $"Weir could not start on port {port}, so it is still at port {current}. See tray-host.log in the data folder.",
                    ToolTipIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            TrayLog.Write($"Change port: stopped before port {port} was ready, because Weir is quitting or updating.");
        }
        finally
        {
            _portMenuItem.Enabled = true;
            _portMenuItem.Text = PortMenuText();
        }
    }

    // -- Browser ------------------------------------------------------------

    private void OpenBrowserDebounced(string source, string relativePath = "/")
    {
        var now = Environment.TickCount64;
        lock (_browserLock)
        {
            if (now - _lastBrowserOpenTicks < BrowserDebounceMs)
            {
                TrayLog.Write($"Ignoring duplicate browser open request within debounce window (source={source}).");
                return;
            }
            _lastBrowserOpenTicks = now;
        }
        TrayLog.Write($"Opening Weir in browser on port {_server.Port} (source={source})");
        Program.OpenBrowser(_server.Port, relativePath: relativePath);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _server.Dispose();
        _notifyIcon?.Dispose();
        _cts.Dispose();
    }
}
