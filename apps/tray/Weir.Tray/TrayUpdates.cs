namespace Weir.Tray;

/// <summary>
/// When the tray checks for, downloads and applies updates, following update-settings.json, and when the server asks
/// for one of those steps now (a person pressed a button in System › About). It runs off the UI thread and reports
/// through callbacks that the tray runs on its UI thread, and through update-state.json, which it rewrites at each
/// step. In Automatic mode a downloaded update is applied once Weir has been idle for a while (#875); in the other
/// modes only a person, or a quit or start, installs it.
/// </summary>
sealed class TrayUpdates
{
    private const string CheckNowFlagFileName = "update-check-now";
    private const string DownloadNowFlagFileName = "update-download-now";
    private const string ApplyNowFlagFileName = "update-apply-now";
    private const string NoNewerVersion = "There is no newer version of Weir to download.";

    private readonly string _runtimeHome;
    private readonly UpdateSettings _settings;
    private readonly IUpdateService _service;
    private readonly UpdateCallbacks _callbacks;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _shutdown;
    private readonly Lock _idleWatchLock = new();
    private readonly Lock _requestsLock = new();
    private int _activity;
    private Task? _idleWatch;

    /// <param name="service">Velopack: checking for updates and downloading them. A download installs nothing.</param>
    /// <param name="runtimeHome">Where update-state.json, the request flags and the server's work-state.json live.</param>
    /// <param name="settings">The operator's update choices.</param>
    /// <param name="callbacks">How the rest of the tray hears about updates and applies them.</param>
    /// <param name="clock">Times the wait for Weir to be idle and the log of a failing watcher.</param>
    /// <param name="shutdown">Cancelled when the tray is ending, which ends every loop here.</param>
    internal TrayUpdates(
        IUpdateService service,
        string runtimeHome,
        UpdateSettings settings,
        UpdateCallbacks callbacks,
        TimeProvider clock,
        CancellationToken shutdown)
    {
        _service = service;
        _runtimeHome = runtimeHome;
        _settings = settings;
        _callbacks = callbacks;
        _clock = clock;
        _shutdown = shutdown;
    }

    internal bool IsDownloaded => _service.IsDownloaded;

    internal string? PendingVersion => _service.PendingVersion;

    /// <summary>
    /// The wait for Weir to be idle so that a downloaded update can install itself, or null when none was started. It
    /// is complete once the update has been handed over for installing or the wait has ended without one.
    /// </summary>
    internal Task? IdleWatch
    {
        get
        {
            lock (_idleWatchLock)
            {
                return _idleWatch;
            }
        }
    }

    internal UpdateMenuState MenuState() =>
        UpdateMenuState.Describe(
            _service.IsInstalled,
            (UpdateActivity)Volatile.Read(ref _activity),
            _service.HasPendingUpdate,
            _service.IsDownloaded,
            _service.PendingVersion);

    /// <summary>Starts the start-up check, the periodic check and the watch for requests, as the settings say.</summary>
    internal void Start()
    {
        if (!_service.IsInstalled)
        {
            TrayLog.Write("Velopack: not installed (dev mode), skipping update checks.");
            return;
        }

        // This process has downloaded nothing yet. A file left by the run before it (one that downloaded the update now
        // installed) would otherwise keep telling the server an update is waiting until a check next finishes.
        WriteState(UpdatePhase.Idle, null);

        // The one look at the flags a person left before this run is the watcher's first.
        var requests = new UpdateRequestWatcher(
            _runtimeHome,
            () => BackgroundWork.Forget("Update requests", () =>
            {
                ActOnRequests();
                return Task.CompletedTask;
            }),
            TrayLog.Write,
            _clock);
        _shutdown.Register(requests.Dispose);
        requests.Start();

        if (_settings.CheckOnStartup)
        {
            CheckInBackground();
        }

        if (_settings.CheckIntervalMinutes > 0)
        {
            var interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
            BackgroundWork.RunLoop("Periodic update check", ct => CheckPeriodicallyAsync(interval, ct), _shutdown);
        }

    }

    internal void CheckInBackground() => BackgroundWork.Forget("Update check", CheckAsync);

    internal void DownloadInBackground() => BackgroundWork.Forget("Update download", DownloadAsync);

    private async Task CheckPeriodicallyAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await CheckAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Does what the flag files in the runtime home ask for, once each: System › About asks for a step by creating one
    /// (check now, download now, or restart and apply). A check or download that is already under way is not started again.
    /// </summary>
    internal void ActOnRequests()
    {
        lock (_requestsLock)
        {
            if (_service.IsDownloaded && Take(ApplyNowFlagFileName))
            {
                TrayLog.Write("Apply-now flag detected - applying update and restarting.");
                _callbacks.OnUi(_callbacks.ApplyNow);
            }
            if (Take(CheckNowFlagFileName))
            {
                TrayLog.Write("Check-now flag detected - checking for an update.");
                CheckInBackground();
            }
            if (Take(DownloadNowFlagFileName))
            {
                TrayLog.Write("Download-now flag detected - downloading the update.");
                DownloadInBackground();
            }
        }
    }

    // The flag asked for its step, so the step is taken even when the flag cannot be removed.
    private bool Take(string flagFileName)
    {
        var flagPath = Path.Combine(_runtimeHome, flagFileName);
        if (!File.Exists(flagPath))
        {
            return false;
        }
        try
        {
            File.Delete(flagPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not delete {flagPath}: {ex.Message}");
        }
        return true;
    }

    // Automatic mode only: the person who chose it is not there to press Restart to update.
    private void InstallWhenIdle()
    {
        lock (_idleWatchLock)
        {
            if (_idleWatch is { IsCompleted: false })
            {
                return;
            }
            TrayLog.Write($"Update v{PendingVersion} downloaded; installing once Weir has been idle for {IdleInstall.IdlePeriodText}.");
            _idleWatch = BackgroundWork.RunLoop("Update install when idle", WaitForIdleThenInstallAsync, _shutdown);
        }
    }

    private async Task WaitForIdleThenInstallAsync(CancellationToken cancellationToken)
    {
        await new IdleInstall(_runtimeHome, _clock).WaitUntilIdleAsync(cancellationToken).ConfigureAwait(false);
        if (!StillInstallsByItself())
        {
            return;
        }
        TrayLog.Write($"Weir has been idle for {IdleInstall.IdlePeriodText}; installing update v{PendingVersion} now.");
        _callbacks.OnUi(_callbacks.ApplyNow);
    }

    // The choice is read again here: the operator may have switched to Download only or Notify only while the update
    // waited, and this is the last point before Weir restarts.
    private bool StillInstallsByItself()
    {
        var mode = UpdateSettings.Load(_runtimeHome).Mode;
        if (mode != UpdateMode.Auto)
        {
            TrayLog.Write($"Update v{PendingVersion} is waiting, but the update mode is now {mode}, so Weir does not install it by itself.");
            return false;
        }
        return _service.IsDownloaded;
    }

    internal async Task CheckAsync()
    {
        // A downloaded update waits to be installed; looking again would make the tray forget it.
        if (_service.IsDownloaded || !TryBegin(UpdateActivity.Checking))
        {
            return;
        }
        try
        {
            var check = await _service.CheckForUpdateAsync().ConfigureAwait(false);
            if (!check.Done)
            {
                WriteState(check.Failure is null ? UpdatePhase.Idle : UpdatePhase.Failed, PendingVersion, check.Failure);
                return;
            }

            var mode = _settings.Mode;
            if (mode == UpdateMode.NotifyOnly)
            {
                WriteState(UpdatePhase.Idle, PendingVersion);
                _callbacks.OnUi(() => _callbacks.Announce(UpdateMode.NotifyOnly));
                return;
            }

            SetActivity(UpdateActivity.Downloading);
            await DownloadFoundAsync(mode).ConfigureAwait(false);
        }
        finally
        {
            SetActivity(UpdateActivity.Idle);
        }
    }

    // A person's click is the choice to download, so this runs in every mode, and finds the update first when the tray
    // has not looked yet.
    internal async Task DownloadAsync()
    {
        if (!TryBegin(UpdateActivity.Downloading))
        {
            return;
        }
        try
        {
            if (!_service.HasPendingUpdate)
            {
                var check = await _service.CheckForUpdateAsync().ConfigureAwait(false);
                if (!check.Done)
                {
                    WriteState(UpdatePhase.Failed, PendingVersion, check.Failure ?? NoNewerVersion);
                    return;
                }
            }

            await DownloadFoundAsync(UpdateMode.DownloadOnly).ConfigureAwait(false);
        }
        finally
        {
            SetActivity(UpdateActivity.Idle);
        }
    }

    private async Task DownloadFoundAsync(UpdateMode announce)
    {
        var download = await _service.DownloadUpdateAsync().ConfigureAwait(false);
        if (!download.Done)
        {
            WriteState(UpdatePhase.Failed, PendingVersion, download.Failure ?? NoNewerVersion);
            return;
        }

        WriteState(UpdatePhase.Downloaded, PendingVersion);
        if (_settings.Mode == UpdateMode.Auto)
        {
            InstallWhenIdle();
        }
        _callbacks.OnUi(() => _callbacks.Announce(announce));
    }

    // One check or download at a time: the periodic check, a menu click and a button in System can otherwise overlap.
    private bool TryBegin(UpdateActivity activity)
    {
        if (Interlocked.CompareExchange(ref _activity, (int)activity, (int)UpdateActivity.Idle) != (int)UpdateActivity.Idle)
        {
            return false;
        }
        ShowActivity(activity);
        return true;
    }

    private void SetActivity(UpdateActivity activity)
    {
        Volatile.Write(ref _activity, (int)activity);
        ShowActivity(activity);
    }

    // Going idle writes nothing: whatever ended the activity has already written how it ended.
    private void ShowActivity(UpdateActivity activity)
    {
        if (activity != UpdateActivity.Idle)
        {
            WriteState(activity == UpdateActivity.Checking ? UpdatePhase.Checking : UpdatePhase.Downloading, PendingVersion);
        }
        _callbacks.OnUi(_callbacks.Changed);
    }

    // Read by the server, which shows it in System › About.
    private void WriteState(UpdatePhase phase, string? version, string? failure = null)
    {
        try
        {
            UpdateStateFile.Write(_runtimeHome, phase, version, failure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not write update state: {ex.Message}");
        }
    }
}
