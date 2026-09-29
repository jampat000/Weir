using Velopack;
using Velopack.Sources;

namespace Weir.Tray;

/// <summary>Checks for, downloads and applies Weir updates from the GitHub releases, through Velopack.</summary>
sealed class UpdateService : IUpdateService
{
    internal const string GitHubRepo = "https://github.com/jampat000/Weir";

    private readonly UpdateManager _mgr;
    private readonly Action<string> _log;
    private volatile UpdateInfo? _pendingUpdate;
    private volatile bool _downloaded;
    private volatile int _downloadProgress;

    public bool HasPendingUpdate => _pendingUpdate is not null;
    public bool IsDownloaded => _downloaded;
    internal int DownloadProgress => _downloadProgress;
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    internal UpdateService(Action<string> log)
    {
        _log = log;
        var source = new GithubSource(GitHubRepo, accessToken: null, prerelease: false);
        _mgr = new UpdateManager(source);
    }

    public bool IsInstalled => _mgr.IsInstalled;

    // Velopack reports every failure (network, GitHub, disk) as an exception; each one means "no update this time"
    // and is logged, so these return false rather than throw.
    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            _log("Checking for updates...");
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                _log("No update available.");
                _pendingUpdate = null;
                _downloaded = false;
                return false;
            }
            _pendingUpdate = info;
            _downloaded = false;
            _downloadProgress = 0;
            _log($"Update available: v{info.TargetFullRelease.Version}");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Update check failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DownloadUpdateAsync()
    {
        var pending = _pendingUpdate;
        if (pending is null)
        {
            return false;
        }
        try
        {
            _log($"Downloading update v{pending.TargetFullRelease.Version}...");
            await _mgr.DownloadUpdatesAsync(pending, p => _downloadProgress = p).ConfigureAwait(false);
            _downloaded = true;
            _log("Update downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Update download failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// What Weir is started again with after an update (#638). An update restart is never a person opening Weir:
    /// in Automatic mode nobody may be at the computer, and "Update now" is pressed in a browser already showing
    /// Weir, so neither should open a window.
    /// </summary>
    internal static string[] RestartArguments() => [Program.NoBrowserArgument];

    public string? FindUpdateLeftWaiting()
    {
        if (!IsInstalled)
        {
            return null;
        }
        try
        {
            return _mgr.UpdatePendingRestart?.Version?.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Could not look for an update left waiting: {ex.Message}");
            return null;
        }
    }

    public void ApplyAndExit()
    {
        var pending = _pendingUpdate;
        if (!_downloaded || pending is null)
        {
            return;
        }
        _log($"Applying update v{pending.TargetFullRelease.Version} and exiting...");
        _mgr.ApplyUpdatesAndExit(pending.TargetFullRelease);
    }

    public void ApplyAndRestart()
    {
        var pending = _pendingUpdate;
        if (!_downloaded || pending is null)
        {
            return;
        }
        _log($"Applying update v{pending.TargetFullRelease.Version} and restarting...");
        _mgr.ApplyUpdatesAndRestart(pending.TargetFullRelease, RestartArguments());
    }

    public void ApplyLeftWaitingUpdateAndRestart()
    {
        var waiting = _mgr.UpdatePendingRestart;
        if (waiting is null)
        {
            return;
        }
        _log($"Applying update v{waiting.Version} left waiting, and restarting...");
        _mgr.WaitExitThenApplyUpdates(waiting, silent: true, restart: true, RestartArguments());
    }
}
