using System.Text.Json;

namespace Weir.Tray;

/// <summary>
/// When the tray checks for, downloads and applies updates, following update-settings.json. It runs off the UI
/// thread and reports through callbacks that the tray runs on its UI thread. In Automatic mode a downloaded update
/// is applied once Weir has been idle for a while (#875); in the other modes only a person, or a quit or start,
/// installs it.
/// </summary>
sealed class TrayUpdates
{
    private const string UpdateStateFileName = "update-state.json";
    private const string ApplyNowFlagFileName = "update-apply-now";

    private static readonly TimeSpan ApplyNowPollInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions UpdateStateJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _runtimeHome;
    private readonly UpdateSettings _settings;
    private readonly IUpdateService _service;
    private readonly UpdateCallbacks _callbacks;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _shutdown;
    private readonly Lock _idleWatchLock = new();
    private int _activity;
    private Task? _idleWatch;

    /// <param name="service">Velopack: checking for updates and downloading them. A download installs nothing.</param>
    /// <param name="runtimeHome">Where update-state.json, the apply-now flag and the server's work-state.json live.</param>
    /// <param name="settings">The operator's update choices.</param>
    /// <param name="callbacks">How the rest of the tray hears about updates and applies them.</param>
    /// <param name="clock">Times the wait for Weir to be idle.</param>
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

    /// <summary>Starts the start-up check, the periodic check and the apply-now watcher, as the settings say.</summary>
    internal void Start()
    {
        if (!_service.IsInstalled)
        {
            TrayLog.Write("Velopack: not installed (dev mode), skipping update checks.");
            return;
        }

        if (_settings.CheckOnStartup)
        {
            CheckInBackground();
        }

        if (_settings.CheckIntervalMinutes > 0)
        {
            var interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
            BackgroundWork.RunLoop("Periodic update check", ct => CheckPeriodicallyAsync(interval, ct), _shutdown);
        }

        BackgroundWork.RunLoop("Update apply-now watcher", WatchForApplyNowAsync, _shutdown);
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

    // The server's Settings page asks for "update now" by creating this flag file.
    private async Task WatchForApplyNowAsync(CancellationToken cancellationToken)
    {
        var flagPath = Path.Combine(_runtimeHome, ApplyNowFlagFileName);
        using var timer = new PeriodicTimer(ApplyNowPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!File.Exists(flagPath) || !_service.IsDownloaded)
            {
                continue;
            }
            try
            {
                File.Delete(flagPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Applying anyway is right: the flag asked for it, and the restart replaces this process.
                TrayLog.Write($"Could not delete {flagPath}: {ex.Message}");
            }
            TrayLog.Write("Apply-now flag detected - applying update and restarting.");
            _callbacks.OnUi(_callbacks.ApplyNow);
            return;
        }
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

    private async Task CheckAsync()
    {
        if (!TryBegin(UpdateActivity.Checking))
        {
            return;
        }
        try
        {
            if (!await _service.CheckForUpdateAsync().ConfigureAwait(false))
            {
                WriteUpdateState(false);
                return;
            }

            if (_settings.Mode == UpdateMode.NotifyOnly)
            {
                _callbacks.OnUi(() => _callbacks.Announce(UpdateMode.NotifyOnly));
                return;
            }

            SetActivity(UpdateActivity.Downloading);
            if (await _service.DownloadUpdateAsync().ConfigureAwait(false))
            {
                WriteUpdateState(true, _service.PendingVersion);
                var mode = _settings.Mode;
                if (mode == UpdateMode.Auto)
                {
                    InstallWhenIdle();
                }
                _callbacks.OnUi(() => _callbacks.Announce(mode));
            }
        }
        finally
        {
            SetActivity(UpdateActivity.Idle);
        }
    }

    private async Task DownloadAsync()
    {
        if (!TryBegin(UpdateActivity.Downloading))
        {
            return;
        }
        try
        {
            if (await _service.DownloadUpdateAsync().ConfigureAwait(false))
            {
                WriteUpdateState(true, _service.PendingVersion);
                if (_settings.Mode == UpdateMode.Auto)
                {
                    InstallWhenIdle();
                }
                _callbacks.OnUi(() => _callbacks.Announce(UpdateMode.DownloadOnly));
            }
        }
        finally
        {
            SetActivity(UpdateActivity.Idle);
        }
    }

    // One check or download at a time: the periodic check and a menu click can otherwise overlap.
    private bool TryBegin(UpdateActivity activity)
    {
        if (Interlocked.CompareExchange(ref _activity, (int)activity, (int)UpdateActivity.Idle) != (int)UpdateActivity.Idle)
        {
            return false;
        }
        _callbacks.OnUi(_callbacks.Changed);
        return true;
    }

    private void SetActivity(UpdateActivity activity)
    {
        Volatile.Write(ref _activity, (int)activity);
        _callbacks.OnUi(_callbacks.Changed);
    }

    // Read by the server's Settings page.
    private void WriteUpdateState(bool downloaded, string? version = null)
    {
        var path = Path.Combine(_runtimeHome, UpdateStateFileName);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { downloaded, version }, UpdateStateJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrayLog.Write($"Could not write update state: {ex.Message}");
        }
    }
}
