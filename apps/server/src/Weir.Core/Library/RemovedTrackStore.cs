using Weir.Core.Rules;

namespace Weir.Core.Library;

/// <summary>
/// Identifies one file's removed-track history the same way <c>files</c> identifies the file
/// itself: a library id (null when the file predates library tracking or came from the watched-folder
/// pipeline) plus the path relative to that library's root.
/// </summary>
public sealed record RemovedTrackFileKey(long? LibraryId, string RelativePath)
{
    public override string ToString() => LibraryId is { } id ? $"{id}:{RelativePath}" : RelativePath;
}

/// <summary>
/// Where the removed tracks a Processing pass recorded for a file are kept (#509). There is no table of
/// its own (the records can move to one in a later migration): an implementation either keeps the records
/// in memory (lost when the process restarts — see <c>InMemoryRemovedTrackStore</c>) or reads them back out
/// of a JSON column a pass already writes into, such as <c>file_logs.detail_json</c> (see
/// <c>Weir.Infrastructure.Library.FileLogRemovedTrackStore</c>'s remarks for why that one was chosen and
/// what it lacks).
/// </summary>
public interface IRemovedTrackStore
{
    /// <summary>Record what one Processing pass removed from one file, replacing whatever was recorded for it before.</summary>
    Task RecordAsync(RemovedTrackFileKey key, IReadOnlyList<RemovedTrackRecord> tracks, CancellationToken cancellationToken = default);

    /// <summary>The most recently recorded removed tracks for one file; empty when none are recorded.</summary>
    Task<IReadOnlyList<RemovedTrackRecord>> GetAsync(RemovedTrackFileKey key, CancellationToken cancellationToken = default);

    /// <summary>Every file this store currently has removed tracks recorded for, keyed the same way.</summary>
    Task<IReadOnlyDictionary<RemovedTrackFileKey, IReadOnlyList<RemovedTrackRecord>>> GetAllAsync(CancellationToken cancellationToken = default);
}
