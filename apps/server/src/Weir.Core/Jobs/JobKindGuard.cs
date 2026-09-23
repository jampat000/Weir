using Weir.Core.Json;

namespace Weir.Core.Jobs;

/// <summary>
/// Which <c>job_kind</c> strings may sit in <c>jobs</c> and be run by its workers.
/// </summary>
/// <remarks>
/// Every durable job is a <c>processing.*</c> kind. Retired prefixes are listed by name so a queue row
/// left by an older install is refused instead of being claimed by a worker that does not know
/// what it is. Matching is ordinal and case-sensitive.
/// </remarks>
public static class JobKindGuard
{
    public const string JobKindPrefix = "processing.";

    /// <summary>Trimmer was removed long ago; its rows are refused.</summary>
    public const string RetiredTrimmerPrefix = "trimmer.";

    /// <summary>Subber moved to Deluno.</summary>
    public const string RetiredSubberPrefix = "subber.";

    /// <summary>Pruner moved to Deluno too (#473).</summary>
    public const string RetiredPrunerPrefix = "pruner.";

    /// <summary>Processing's supplied-payload evaluation family, removed in #339.</summary>
    public const string RetiredSuppliedPayloadEvaluationPrefix = "processing.supplied_payload_evaluation.";

    /// <summary>Processing's candidate gate, reshaped into a synchronous endpoint in #339.</summary>
    public const string RetiredCandidateGatePrefix = "processing.candidate_gate.";

    public static readonly IReadOnlyList<string> RetiredPrefixes =
    [
        RetiredTrimmerPrefix,
        RetiredSubberPrefix,
        RetiredPrunerPrefix,
        RetiredSuppliedPayloadEvaluationPrefix,
        RetiredCandidateGatePrefix,
    ];

    /// <summary>The kind belongs to a retired family and must never run.</summary>
    public static bool IsRetired(string jobKind)
    {
        ArgumentNullException.ThrowIfNull(jobKind);
        return RetiredPrefixes.Any(prefix => jobKind.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool HasProcessingPrefix(string jobKind)
    {
        ArgumentNullException.ThrowIfNull(jobKind);
        return jobKind.StartsWith(JobKindPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// A kind no worker may run: retired, or missing the <c>processing.</c> prefix. Workers claim these
    /// only to refuse them, so the row is failed with a reason rather than left in the queue.
    /// </summary>
    public static bool IsRefused(string jobKind) => IsRetired(jobKind) || !HasProcessingPrefix(jobKind);

    /// <summary>Checks an enqueue: queue rows must be live <c>processing.*</c> kinds.</summary>
    /// <exception cref="ArgumentException">The kind is retired or not a <c>processing.*</c> kind.</exception>
    public static void ValidateEnqueueJobKind(string jobKind)
    {
        ArgumentNullException.ThrowIfNull(jobKind);
        if (IsRetired(jobKind))
        {
            throw new ArgumentException(
                $"processing_enqueue_or_get_job refuses a retired job_kind (got {PyStrings.Repr(jobKind)})");
        }

        if (!HasProcessingPrefix(jobKind))
        {
            throw new ArgumentException(
                $"processing_enqueue_or_get_job requires job_kind to start with {PyStrings.Repr(JobKindPrefix)} (got {PyStrings.Repr(jobKind)})");
        }
    }

    /// <summary>Checks the worker's handler registry: handlers only for live <c>processing.*</c> kinds.</summary>
    /// <exception cref="ArgumentException">A key is retired or not a <c>processing.*</c> kind.</exception>
    public static void ValidateHandlerRegistry(IEnumerable<string> jobKinds)
    {
        ArgumentNullException.ThrowIfNull(jobKinds);
        var bad = jobKinds.Where(IsRefused).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (bad.Count > 0)
        {
            throw new ArgumentException(
                "Worker handler registry keys must start with " +
                $"{PyStrings.Repr(JobKindPrefix)} and must not use a retired prefix (offending keys: [{string.Join(", ", bad.Select(PyStrings.Repr))}])");
        }
    }
}
