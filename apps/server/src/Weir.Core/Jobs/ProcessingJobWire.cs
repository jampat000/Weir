using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Core.Jobs;

/// <summary>A job as the API returns it, wherever it is listed: the jobs inspection list and System › Logs.</summary>
public static class ProcessingJobWire
{
    public static WireObject Out(ProcessingJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var (message, nextAction, technicalDetail) = OperatorJobStatus.Build("processing", job.JobKind, job.Status, job.LastError, job.PayloadJson);
        return new WireObject()
            .Set("id", job.Id)
            .Set("dedupe_key", job.DedupeKey)
            .Set("job_kind", job.JobKind)
            .Set("status", job.Status)
            .Set("attempt_count", job.AttemptCount)
            .Set("max_attempts", job.MaxAttempts)
            .Set("lease_owner", job.LeaseOwner)
            .Set("lease_expires_at", job.LeaseExpiresAt is { } lease ? Timestamp.FromDateTimeOffset(lease).ToWireText() : null)
            .Set("last_error", job.LastError)
            .Set("operator_message", message)
            .Set("next_action", nextAction)
            .Set("technical_detail", technicalDetail)
            .Set("payload_json", job.PayloadJson)
            .Set("created_at", Timestamp.FromDateTimeOffset(job.CreatedAt).ToWireText())
            .Set("updated_at", Timestamp.FromDateTimeOffset(job.UpdatedAt).ToWireText());
    }
}
