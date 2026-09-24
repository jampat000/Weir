namespace Weir.Infrastructure.Processing;

/// <summary>
/// A count of committed changes to the libraries themselves (created, edited, deleted, imported, restored), so the
/// filesystem watcher can pick one up straight away instead of re-reading every library on a short timer (#720).
/// </summary>
public sealed class LibraryChanges
{
    private long _version;

    /// <summary>Goes up by one for every <see cref="Record"/>.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Call after the change has committed.</summary>
    public void Record() => Interlocked.Increment(ref _version);
}
