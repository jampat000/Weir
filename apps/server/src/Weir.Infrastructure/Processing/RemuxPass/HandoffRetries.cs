using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// The retry of a hand-off's file that failed, in a workflow a linked Deluno feeds. For every other workflow the next scan
/// notices a failed file whose retry is due and queues it again; Weir's scan never queues work for a Deluno workflow, so the
/// failed hand-off queues its own next attempt, to start when its backoff ends, carrying the hand-off's origin.
/// </summary>
internal static class HandoffRetries
{
    /// <summary>Queues the next attempt when <paramref name="decision"/> asks for one, the file came from a hand-off, and the scan will not do it.</summary>
    public static async Task QueueIfOwedAsync(
        UnitOfWork uow, IFailurePolicy policy, LibraryStore libraries, ProcessingLibraryRecord library, string relativePath, WireObject? origin, RetryDecision decision)
    {
        if (!decision.WillRetry || decision.NextRetryAt is not { } startsAt || origin is not { IsTruthy: true })
        {
            return;
        }

        if ((await libraries.ManagerLinksAsync(uow, library.Id).ConfigureAwait(false)).HandedOffByManager)
        {
            await policy.QueueHandoffRetryAsync(uow, library, relativePath, origin, startsAt).ConfigureAwait(false);
        }
    }
}
