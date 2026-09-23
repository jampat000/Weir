using Velopack;
using Velopack.Sources;

namespace Weir.Tray;

/// <summary>Checks for, downloads and applies Weir updates from the GitHub releases, through Velopack.</summary>
sealed class UpdateService
{
    internal const string GitHubRepo = "https://github.com/jampat000/Weir";

    private readonly UpdateManager _mgr;
    private readonly Action<string> _log;
    private volatile UpdateInfo? _pendingUpdate;
    private volatile bool _downloaded;
    private volatile int _downloadProgress;

    internal bool HasPendingUpdate => _pendingUpdate is not null;
    internal bool IsDownloaded => _downloaded;
    internal int DownloadProgress => _downloadProgress;
    internal string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    internal UpdateService(Action<string> log)
    {
        _log = log;
        var source = new GithubSource(GitHubRepo, accessToken: null, prerelease: false);
        _mgr = new UpdateManager(source);
    }

    internal bool IsInstalled => _mgr.IsInstalled;

    // Velopack reports every failure (network, GitHub, disk) as an exception; each one means "no update this time"
    // and is logged, so these return false rather than throw.
    internal async Task<bool> CheckForUpdateAsync()
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

    internal async Task<bool> DownloadUpdateAsync()
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

    internal void ApplyAndRestart()
    {
        var pending = _pendingUpdate;
        if (!_downloaded || pending is null)
        {
            return;
        }
        _log($"Applying update v{pending.TargetFullRelease.Version} and restarting...");
        _mgr.ApplyUpdatesAndRestart(pending.TargetFullRelease, RestartArguments());
    }

    internal void ApplyOnExit()
    {
        var pending = _pendingUpdate;
        if (!_downloaded || pending is null)
        {
            return;
        }
        _log($"Scheduling update v{pending.TargetFullRelease.Version} to apply on exit...");
        _mgr.WaitExitThenApplyUpdates(pending.TargetFullRelease, silent: true, restart: true, RestartArguments());
    }
}
