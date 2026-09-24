using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Records the file side of a handler crash: the failure against the file and the library's failure
/// policy, which the processing engine owns (#522).
/// </summary>
public interface IUnhandledJobFailureRecorder
{
    /// <summary>
    /// Record the failure against the file and apply the library's failure policy. Returns whether the
    /// file will be retried, or null when there was no library or file to record against.
    /// </summary>
    Task<bool?> RecordAsync(UnhandledJobFailure failure, CancellationToken cancellationToken);
}

/// <summary>A handler failure the handler did not record itself.</summary>
public sealed record UnhandledJobFailure(JobWorkContext Context, long? LibraryId, string MediaScope, string? RelativeMediaPath, string Message);

/// <summary>Records nothing; used when no file-state policy is registered.</summary>
public sealed class NoUnhandledJobFailureRecorder : IUnhandledJobFailureRecorder
{
    public Task<bool?> RecordAsync(UnhandledJobFailure failure, CancellationToken cancellationToken) => Task.FromResult<bool?>(null);
}
