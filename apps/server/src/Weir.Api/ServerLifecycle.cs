namespace Weir.Api;

/// <summary>
/// Startup facts that health and readiness report: when startup began, whether it finished, and whether the
/// database was opened.
/// </summary>
public sealed class ServerLifecycle
{
    private readonly TimeProvider _time;
    private readonly long _startedAt;
    private volatile bool _databaseOpened;
    private volatile bool _startupComplete;

    public ServerLifecycle(TimeProvider time)
    {
        _time = time;
        _startedAt = time.GetTimestamp();
    }

    public TimeSpan Elapsed => _time.GetElapsedTime(_startedAt);

    /// <summary>The database passed its schema check and can be queried.</summary>
    public bool DatabaseOpened => _databaseOpened;

    /// <summary>Startup finished and shutdown has not begun.</summary>
    public bool StartupComplete => _startupComplete;

    public void MarkDatabaseOpened() => _databaseOpened = true;

    public void MarkStartupComplete() => _startupComplete = true;

    public void MarkStopping() => _startupComplete = false;
}
