using System.Collections.Concurrent;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// How many files each running library scan has looked at so far, so the Library screen can count up while a long walk runs
/// instead of waiting for the index to be written. Held in memory only: a scan that is not running has nothing to report.
/// </summary>
public sealed class LibraryScanProgress
{
    private readonly ConcurrentDictionary<long, long> _filesSeen = new();

    /// <summary>The scan job <paramref name="jobId"/> has looked at <paramref name="filesSeen"/> files so far.</summary>
    public void Report(long jobId, long filesSeen) => _filesSeen[jobId] = filesSeen;

    /// <summary>The scan job has ended, worked or not.</summary>
    public void Clear(long jobId) => _filesSeen.TryRemove(jobId, out _);

    /// <summary>The files the scan job has looked at so far, or null when it has reported none (queued, or not running).</summary>
    public long? FilesSeen(long jobId) => _filesSeen.TryGetValue(jobId, out var seen) ? seen : null;
}
