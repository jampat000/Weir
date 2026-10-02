using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>Enqueue-or-get by dedupe key.</summary>
public sealed partial class ProcessingJobStore
{
    /// <summary>
    /// Insert a pending job, or return the row already holding <paramref name="dedupeKey"/>. A null <paramref name="runnerCost"/>
    /// costs the job against the resolution budget as <see cref="RunnerCosts"/> says.
    /// </summary>
    /// <exception cref="ArgumentException">The job kind is retired or not a <c>processing.*</c> kind.</exception>
    public Task<ProcessingJob> EnqueueOrGetAsync(
        string dedupeKey,
        string jobKind,
        string? payloadJson = null,
        int maxAttempts = JobQueueRules.DefaultMaxAttempts,
        int? runnerCost = null,
        int priority = 0,
        DateTimeOffset? notBefore = null,
        CancellationToken cancellationToken = default)
    {
        JobKindGuard.ValidateEnqueueJobKind(jobKind);
        ArgumentNullException.ThrowIfNull(dedupeKey);
        return InTransactionAsync(
            (connection, transaction) => EnqueueOrGet(connection, transaction, dedupeKey, jobKind, payloadJson, maxAttempts, runnerCost, priority, notBefore),
            cancellationToken);
    }

    /// <summary>
    /// Queue a job that runs again and again under one key, one run at a time: a run still pending or leased under
    /// <paramref name="dedupeKey"/> is returned as it is, and a finished run gives its key up, keeping its row as history,
    /// so a new run is queued.
    /// </summary>
    /// <exception cref="ArgumentException">The job kind is retired or not a <c>processing.*</c> kind.</exception>
    public Task<ProcessingJob> EnqueueNextRunAsync(
        string dedupeKey,
        string jobKind,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        JobKindGuard.ValidateEnqueueJobKind(jobKind);
        ArgumentNullException.ThrowIfNull(dedupeKey);
        return InTransactionAsync(
            (connection, transaction) =>
            {
                if (GetByDedupeKey(connection, transaction, dedupeKey) is { } last && ProcessingJobStatus.Terminal.Contains(last.Status))
                {
                    Execute(
                        connection,
                        transaction,
                        "UPDATE jobs SET dedupe_key = @dedupe WHERE id = @id",
                        ("@dedupe", JobQueueRules.TombstoneFinishedDedupeKey(last.DedupeKey, last.Id)),
                        ("@id", last.Id));
                }

                return EnqueueOrGet(connection, transaction, dedupeKey, jobKind, payloadJson, JobQueueRules.DefaultMaxAttempts, runnerCost: null, priority: 0);
            },
            cancellationToken);
    }

    internal ProcessingJob EnqueueOrGet(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string dedupeKey,
        string jobKind,
        string? payloadJson,
        int maxAttempts,
        int? runnerCost,
        int priority,
        DateTimeOffset? notBefore = null)
    {
        JobKindGuard.ValidateEnqueueJobKind(jobKind);
        var existing = GetByDedupeKey(connection, transaction, dedupeKey);
        if (existing is not null)
        {
            return existing;
        }

        // not_before is the column a retry's backoff already uses, so a job queued to start later is claimed by
        // exactly the same rule as one that is waiting out a failure (#632).
        var inserted = Scalar(
            connection,
            transaction,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, max_attempts, runner_cost, priority, not_before) " +
            "VALUES (@dedupe, @kind, @payload, @status, @max_attempts, @runner_cost, @priority, @not_before) " +
            "ON CONFLICT (dedupe_key) DO NOTHING RETURNING id",
            ("@dedupe", dedupeKey),
            ("@kind", jobKind),
            ("@payload", payloadJson),
            ("@status", ProcessingJobStatus.Pending),
            ("@max_attempts", Math.Max(1, maxAttempts)),
            ("@runner_cost", Math.Max(0, runnerCost ?? RunnerCosts.ForJob(connection, transaction, jobKind, payloadJson))),
            ("@priority", priority),
            ("@not_before", notBefore is { } when ? TimestampColumns.Orm(when) : null));
        if (inserted is not null and not DBNull)
        {
            RecordQueueDepth(connection, transaction);
            // Woken before this transaction commits, which is safe: a woken slot claims under BEGIN IMMEDIATE, so its claim
            // waits for this write lock and sees the job once it is committed, or nothing if it rolls back.
            _wakeSignals?.WakeAll();
        }

        return GetByDedupeKey(connection, transaction, dedupeKey)
               ?? throw new InvalidOperationException("processing job dedupe race: row missing after IntegrityError");
    }
}
