namespace Weir.Tray;

/// <summary>
/// Tells the tray when System › About asks for an update step, the moment the server leaves a flag file for it
/// (<c>update-check-now</c>, <c>update-download-now</c> or <c>update-apply-now</c> in the runtime home), with no timer. A
/// burst of events is looked at once, after it has settled. When the watcher loses events (its buffer overflowed) or
/// fails, the folder is looked at again at once, since a flag may have arrived unseen; the failure is logged at most
/// once a minute, so a watcher that keeps failing cannot fill the log.
/// </summary>
sealed class UpdateRequestWatcher : IDisposable
{
    /// <summary>How long a burst of events is given to finish before the folder is looked at.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    /// <summary>The least time between two log lines about the watcher itself failing.</summary>
    internal static readonly TimeSpan ErrorLogInterval = TimeSpan.FromMinutes(1);

    // The three flag files and nothing else in the runtime home.
    private const string FlagFilter = "update-*-now";

    // Large enough that a burst of writes in the runtime home does not overflow the watcher's buffer.
    private const int WatcherBufferBytes = 64 * 1024;

    private readonly Action _look;
    private readonly Action<string> _log;
    private readonly TimeProvider _clock;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _settle;
    private long? _lastErrorLogged;

    /// <param name="runtimeHome">The folder the flag files are left in.</param>
    /// <param name="look">Called, on a thread-pool thread, to look for flag files: once at start and after each burst of events.</param>
    /// <param name="log">Where a failing watcher says so.</param>
    /// <param name="clock">Times the least gap between two of those lines.</param>
    internal UpdateRequestWatcher(string runtimeHome, Action look, Action<string> log, TimeProvider clock)
    {
        _look = look;
        _log = log;
        _clock = clock;
        _settle = new System.Threading.Timer(_ => _look());
        _watcher = new FileSystemWatcher(runtimeHome, FlagFilter)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            InternalBufferSize = WatcherBufferBytes,
        };
        _watcher.Created += OnEvent;
        _watcher.Changed += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.Error += (_, e) => OnWatcherError(e.GetException());
    }

    internal int BufferBytes => _watcher.InternalBufferSize;

    /// <summary>Starts listening, then looks once, so a flag left before this is not missed.</summary>
    internal void Start()
    {
        _watcher.EnableRaisingEvents = true;
        _look();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _settle.Dispose();
    }

    internal void OnWatcherError(Exception error)
    {
        var now = _clock.GetTimestamp();
        var due = false;
        lock (_settle)
        {
            if (_lastErrorLogged is not { } last || _clock.GetElapsedTime(last, now) >= ErrorLogInterval)
            {
                _lastErrorLogged = now;
                due = true;
            }
        }
        if (due)
        {
            _log($"Watching for update requests failed ({error.Message}); the tray looks for them again now.");
        }
        OnEvent(this, EventArgs.Empty);
    }

    private void OnEvent(object? sender, EventArgs e) => _settle.Change(Settle, Timeout.InfiniteTimeSpan);
}
