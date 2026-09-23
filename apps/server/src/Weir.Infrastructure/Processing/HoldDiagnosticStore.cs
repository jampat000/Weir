using Weir.Core.Processing;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// "Why is this file held?": ask every media manager
/// linked to this file's library what it is doing with it, right now. Deliberately live rather than
/// cached — the question is only ever asked because the recorded state looks wrong or stale, and answering
/// it from the same record would be no answer at all.
/// </summary>
public static class HoldDiagnosticStore
{
    /// <summary>The release title for a relative path: the folder name, or the file stem at the root.</summary>
    public static string ReleaseTitleFromRelativePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[^2];
        }

        if (parts.Length == 0)
        {
            return relativePath;
        }

        var stem = parts[^1];
        var dot = stem.LastIndexOf('.');
        return dot > 0 ? stem[..dot] : stem;
    }

    /// <summary>
    /// Ask every manager linked to <paramref name="library"/> what it is importing, and apply the same
    /// domain rules the watched-folder scan applies per file.
    /// <paramref name="connections"/> is the shared connection-resolution/HTTP service: this call never duplicates
    /// its HTTP or dialect logic.
    /// </summary>
    public static async Task<CandidateGateOutcome> EvaluateAsync(
        UnitOfWork uow, ProcessingFileRecord file, ProcessingLibraryRecord library, MediaManagerConnectionService connections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(connections);
        var scope = ProcessingMediaScopes.Normalize(library.MediaType);
        var connectionIds = await LibraryStore.ManagerConnectionIdsAsync(uow, library.Id).ConfigureAwait(false);
        // Unlike the watched-folder scan (which must ask nobody when a library links nothing), this
        // diagnostic falls back to every connection covering the scope when the library names none.
        var signals = await connections.CollectQueueSignalsAsync(
            uow, scope, connectionIds.Count > 0 ? connectionIds : null, cancellationToken).ConfigureAwait(false);
        var report = ManagerQueueSignals.ReportForSignals(signals);
        var candidate = new FileAnchorCandidate(ReleaseTitleFromRelativePath(file.RelativePath));
        var rows = ManagerQueueSignals.AttributedQueueRows(signals, scope, candidatePath: file.RelativePath);
        return CandidateGate.Evaluate(scope, report, rows, candidate);
    }
}
