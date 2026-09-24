using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>What the post-success cleanups need from the database and the managers, so the file logic stays testable.</summary>
public interface IPostSuccessCleanupData
{
    /// <summary>What each media manager covering the scope reports about its library files.</summary>
    Task<IReadOnlyList<ManagerLibraryTruth>> CollectLibraryTruthAsync(string mediaScope, CancellationToken cancellationToken);

    /// <summary>Every pending or leased <c>processing.file.remux_pass.v1</c> row.</summary>
    Task<IReadOnlyList<ActiveRemuxJob>> ActiveRemuxJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the hand-off ledger already recorded this origin's outcome as delivered
    /// (<c>completed</c> or <c>passed-through</c>) to the manager that asked for it. False for no origin, an origin the
    /// ledger has never heard of, or one still short of that terminal state (#545).
    /// </summary>
    Task<bool> HandoffOutcomeAcknowledgedAsync(HandoffOrigin? origin, CancellationToken cancellationToken);
}
