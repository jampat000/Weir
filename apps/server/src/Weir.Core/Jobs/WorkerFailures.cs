using Weir.Core.Json;
using Weir.Core.Observability;

namespace Weir.Core.Jobs;

/// <summary>
/// The handler already wrote this failure to Activity; the worker still fails the job but does not
/// write a second entry (#488).
/// </summary>
public sealed class AlreadyRecordedFailureException : Exception
{
    /// <summary>The type name written into the stored technical detail, kept stable so stored errors read the same.</summary>
    public const string PythonTypeName = "AlreadyRecordedFailure";

    public AlreadyRecordedFailureException()
    {
    }

    public AlreadyRecordedFailureException(string message)
        : base(message)
    {
    }

    public AlreadyRecordedFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// What a worker says when a job fails (#488).
/// </summary>
/// <remarks>
/// The words match what happens next: a failed attempt is put back in the queue until it has used
/// its attempts, so "marked failed" is only said when it is true. Queue kinds and raw exception text
/// go in the technical part of <c>last_error</c>, after the sentence a person reads.
/// </remarks>
public static class WorkerFailures
{
    public const int ErrorLimit = 10_000;

    public const string WillRetryContinuation = "Weir will try this job again shortly.";
    public const string MarkedFailedContinuation = "This job is marked failed so it does not look successful.";

    /// <summary>Whether another attempt follows this one.</summary>
    public static bool RetryComing(int attemptCount, int maxAttempts) => attemptCount < maxAttempts;

    /// <summary>The operator failure for a job, ending with whether it will be retried or is marked failed.</summary>
    public static OperatorFailure JobFailure(string module, FailureSubject cause, bool willRetry) =>
        FailureMessages.FromException(
            module,
            "job",
            cause,
            continuation: willRetry ? WillRetryContinuation : MarkedFailedContinuation);

    /// <summary>The stored error text: the sentence a person reads first, then what to do, then the technical detail.</summary>
    public static string StoredError(OperatorFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var text = failure.Message;
        if (!string.IsNullOrEmpty(failure.NextAction))
        {
            text += $" Next action: {failure.NextAction}";
        }

        if (!string.IsNullOrEmpty(failure.TechnicalDetail))
        {
            text += $" Technical detail: {failure.TechnicalDetail}";
        }

        return PyStrings.Slice(text, ErrorLimit);
    }

    /// <summary>The stored error for a job this worker cannot run.</summary>
    public static string RefusedJobError(string module, string technicalReason, bool willRetry) =>
        StoredError(JobFailure(module, FailureMessages.RuntimeError(technicalReason), willRetry));

    /// <summary>The worker's refusal of a retired kind.</summary>
    public static string RetiredKindReason(string jobKind, long jobId) =>
        "worker refused a retired job_kind: " +
        $"{PyStrings.Repr(jobKind)} (row id={jobId}); nothing runs this kind any more";

    /// <summary>The worker's refusal of a kind without the <c>processing.</c> prefix.</summary>
    public static string UnprefixedKindReason(string jobKind, long jobId) =>
        "worker refused job_kind missing required processing.* prefix: " +
        $"{PyStrings.Repr(jobKind)} (row id={jobId}); enqueue only processing-owned kinds";

    /// <summary>A job kind with no registered handler, as a failure cause.</summary>
    public static FailureSubject NoHandler(string jobKind) =>
        new("ProcessingNoHandlerForJobKind", $"no job handler registered for job_kind={PyStrings.Repr(jobKind)}", ExceptionCategory.Other);
}
