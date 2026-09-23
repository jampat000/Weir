using System.Text.Json;

namespace Weir.Tray;

/// <summary>
/// When the tray checks for, downloads and applies updates, following update-settings.json. It runs off the UI
/// thread and reports through callbacks that the tray runs on its UI thread.
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
    private readonly UpdateService _service = new(TrayLog.Write);
    private readonly Action<Action> _onUi;
    private readonly Action _changed;
    private readonly Action<UpdateMode> _announce;
    private readonly Action _applyNow;
    private int _activity;

    /// <param name="runtimeHome">Where update-state.json and the apply-now flag live.</param>
    /// <param name="settings">The operator's update choices.</param>
    /// <param name="onUi">Runs an action on the tray's UI thread.</param>
    /// <param name="changed">The menu state may have changed.</param>
    /// <param name="announce">An update was found or downloaded under this mode; tell the person.</param>
    /// <param name="applyNow">The server's Settings page asked to apply the downloaded update now.</param>
    internal TrayUpdates(
        string runtimeHome,
        UpdateSettings settings,
        Action<Action> onUi,
        Action changed,
        Action<UpdateMode> announce,
        Action applyNow)
    {
        _runtimeHome = runtimeHome;
        _settings = settings;
        _onUi = onUi;
        _changed = changed;
        _announce = announce;
        _applyNow = applyNow;
    }

    internal bool IsDownloaded => _service.IsDownloaded;

    internal string? PendingVersion => _service.PendingVersion;

    internal UpdateMenuState MenuState() =>
        UpdateMenuState.Describe(
            _service.IsInstalled,
            (UpdateActivity)Volatile.Read(ref _activity),
            _service.HasPendingUpdate,
            _service.IsDownloaded,
            _service.PendingVersion);

    /// <summary>Starts the start-up check, the periodic check and the apply-now watcher, as the settings say.</summary>
    internal void Start(CancellationToken cancellationToken)
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
            BackgroundWork.RunLoop("Periodic update check", ct => CheckPeriodicallyAsync(interval, ct), cancellationToken);
        }

        BackgroundWork.RunLoop("Update apply-now watcher", WatchForApplyNowAsync, cancellationToken);
    }

    internal void CheckInBackground() => BackgroundWork.Forget("Update check", CheckAsync);

    internal void DownloadInBackground() => BackgroundWork.Forget("Update download", DownloadAsync);

    internal void ApplyAndRestart() => _service.ApplyAndRestart();

    internal void ApplyOnExit() => _service.ApplyOnExit();

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
            _onUi(_applyNow);
            return;
        }
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
                _onUi(() => _announce(UpdateMode.NotifyOnly));
                return;
            }

            SetActivity(UpdateActivity.Downloading);
            if (await _service.DownloadUpdateAsync().ConfigureAwait(false))
            {
                WriteUpdateState(true, _service.PendingVersion);
                if (_settings.Mode == UpdateMode.Auto)
                {
                    _service.ApplyOnExit();
                }
                var mode = _settings.Mode;
                _onUi(() => _announce(mode));
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
                _onUi(() => _announce(UpdateMode.DownloadOnly));
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
        _onUi(_changed);
        return true;
    }

    private void SetActivity(UpdateActivity activity)
    {
        Volatile.Write(ref _activity, (int)activity);
        _onUi(_changed);
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
