namespace Weir.Tray;

/// <summary>
/// Tells the tray when the server rewrites tray-status.json, the moment it does and with no timer. The server writes it
/// whole to a scratch file and renames it into place, which reaches a file watcher as a burst of events; the burst is
/// read once, after it has settled.
/// </summary>
sealed class TrayStatusWatcher : IDisposable
{
    /// <summary>How long a burst of events is given to finish before the file is read.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private readonly string _runtimeHome;
    private readonly Action<TrayStatus?> _changed;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _settle;

    /// <param name="runtimeHome">The folder holding tray-status.json.</param>
    /// <param name="changed">Called, on a thread-pool thread, with the status the file holds now; null when it holds none.</param>
    internal TrayStatusWatcher(string runtimeHome, Action<TrayStatus?> changed)
    {
        _runtimeHome = runtimeHome;
        _changed = changed;
        _settle = new System.Threading.Timer(_ => Announce());
        _watcher = new FileSystemWatcher(runtimeHome, TrayStatusFile.FileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnEvent;
        _watcher.Changed += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Error += (_, e) =>
        {
            TrayLog.Write($"Watching {TrayStatusFile.FileName} failed ({e.GetException().Message}); the tray reads it again now.");
            OnEvent(this, e);
        };
    }

    /// <summary>Starts listening, then reports what the file holds now, so a write made before this is not missed.</summary>
    internal void Start()
    {
        _watcher.EnableRaisingEvents = true;
        Announce();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _settle.Dispose();
    }

    private void OnEvent(object? sender, EventArgs e) => _settle.Change(Settle, Timeout.InfiniteTimeSpan);

    private void Announce() => _changed(TrayStatusFile.Read(_runtimeHome, TrayLog.Write));
}
