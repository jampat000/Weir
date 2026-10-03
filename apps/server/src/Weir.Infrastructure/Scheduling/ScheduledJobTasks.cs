using Weir.Core.Jobs;
using Weir.Core.LibraryMode;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Scheduling;

/// <summary>
/// The tasks whose work runs as queued jobs: a timer queues the job, a worker runs it. Names each task's key and label, and
/// says which task a job belongs to, so the timer and the worker talk about the same row of <see cref="PeriodicTaskRegistry"/>.
/// </summary>
public static class ScheduledJobTasks
{
    /// <summary>The cleanup that clears half-written copies from the work folders.</summary>
    public static readonly CleanupTask LeftoverFiles = new("cleanup-leftover-files", "Clear leftover files", PeriodicJobKinds.WorkTempStaleSweep);

    /// <summary>The cleanup that removes cleaned copies no media manager imported.</summary>
    public static readonly CleanupTask UnclaimedCopies = new("cleanup-unclaimed-copies", "Clear unclaimed copies", PeriodicJobKinds.UnclaimedHandbackCleanup);

    /// <summary>The cleanup families, in the order Settings lists them.</summary>
    public static readonly IReadOnlyList<CleanupTask> Cleanups = [LeftoverFiles, UnclaimedCopies];

    /// <summary>A workflow's periodic look at its watched folder for new downloads.</summary>
    public static string ScanKey(long libraryId) => $"scan-{libraryId}";

    public static string ScanLabel(string workflowName) => $"Scan {workflowName}";

    /// <summary>A workflow's scheduled scan and clean of its library folders.</summary>
    public static string LibraryCleanKey(long libraryId) => $"library-clean-{libraryId}";

    public static string LibraryCleanLabel(string workflowName) => $"Clean {workflowName} library";

    /// <summary>The cleanup family a job kind belongs to; null when it belongs to none.</summary>
    public static CleanupTask? CleanupForJobKind(string jobKind) => Cleanups.FirstOrDefault(cleanup => cleanup.JobKind == jobKind);

    /// <summary>The task a job's run counts towards; null for a job no timer is responsible for.</summary>
    public static string? KeyFor(string jobKind, string? payloadJson)
    {
        ArgumentNullException.ThrowIfNull(jobKind);
        if (CleanupForJobKind(jobKind) is { } cleanup)
        {
            return cleanup.Key;
        }

        if (jobKind is not (ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch or LibraryModeJobKinds.ScanKind or LibraryModeJobKinds.CleanKind) ||
            JobPayload.LibraryIdForAdmission(payloadJson) is not { } libraryId)
        {
            return null;
        }

        return jobKind == ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch ? ScanKey(libraryId) : LibraryCleanKey(libraryId);
    }
}

/// <summary>One cleanup family: the key and label it is listed under, and the kind of job that does its work.</summary>
public sealed record CleanupTask(string Key, string Label, string JobKind);
