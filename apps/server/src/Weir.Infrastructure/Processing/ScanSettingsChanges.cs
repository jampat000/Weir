namespace Weir.Infrastructure.Processing;

/// <summary>
/// A count of committed changes to what the folder watcher and the scan scheduler read: the libraries (created,
/// edited, deleted, imported, restored) and the operator's scan settings. Both pick a change up on their next tick
/// instead of re-reading the database on a short timer (#720).
/// </summary>
/// <remarks>
/// The watcher and scheduler kill switches (<c>WEIR_PROCESSING_WATCHER_ENABLED</c>,
/// <c>WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED</c>) are environment variables read at
/// startup, so they never change while the server runs and need no signal.
/// </remarks>
public sealed class ScanSettingsChanges
{
    private long _version;

    /// <summary>Goes up by one for every <see cref="Record"/>.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Call after the change has committed.</summary>
    public void Record() => Interlocked.Increment(ref _version);
}
