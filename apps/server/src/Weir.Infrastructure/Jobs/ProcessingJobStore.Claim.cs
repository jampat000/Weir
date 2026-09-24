using System.Globalization;
using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>Atomic claim with a lease, gated by admission (pause, schedules, budget) and by claimable kinds.</summary>
public sealed partial class ProcessingJobStore
{
    private const string ClaimSqlTemplate = """
        UPDATE jobs
        SET
          status = @leased,
          lease_owner = @owner,
          lease_expires_at = @lease_exp,
          updated_at = CURRENT_TIMESTAMP,
          attempt_count = attempt_count + 1
        WHERE id = (
          SELECT id FROM jobs
          WHERE (
              (status = @pending AND (not_before IS NULL OR julianday(not_before) <= julianday(@now)))
              OR (
                status = @leased
                AND (lease_expires_at IS NULL OR julianday(lease_expires_at) < julianday(@now))
              )
            )
            {admission}{kinds}
          ORDER BY priority DESC, id ASC
          LIMIT 1
        )
        RETURNING id
        """;

    /// <summary>
    /// Atomically lease the next pending or expired-lease row.
    /// Increments <c>attempt_count</c> on every claim, including a reclaim.
    /// </summary>
    public Task<ProcessingJob?> ClaimNextAsync(
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset? now = null,
        WorkAdmission? admission = null,
        ClaimableKinds? kinds = null,
        WorkLane lane = WorkLane.Files,
        CancellationToken cancellationToken = default)
    {
        var when = now ?? _time.GetUtcNow();
        return InTransactionAsync(
            (connection, transaction) => ClaimNext(connection, transaction, leaseOwner, leaseExpiresAt, when, admission, kinds, lane),
            cancellationToken);
    }

    /// <summary>
    /// The worker's claim: evaluate admission (pause, schedules, budget) and claim in the same write
    /// transaction, so two workers cannot both see room for one more job. <paramref name="lane"/> picks which
    /// admission rules apply: the files lane is gated by the resolution budget and each library's pass limit,
    /// the upkeep lane by neither (#717).
    /// </summary>
    public Task<ProcessingJob?> ClaimNextAdmittedAsync(
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset now,
        ClaimableKinds? kinds,
        WorkLane lane = WorkLane.Files,
        CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            (connection, transaction) =>
            {
                var admission = WorkAdmissionReader.Evaluate(connection, transaction, now);
                return ClaimNext(connection, transaction, leaseOwner, leaseExpiresAt, now, admission, kinds, lane);
            },
            cancellationToken);

    internal ProcessingJob? ClaimNext(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset now,
        WorkAdmission? admission,
        ClaimableKinds? kinds,
        WorkLane lane = WorkLane.Files)
    {
        ArgumentNullException.ThrowIfNull(leaseOwner);
        var parameters = new List<(string, object?)>
        {
            ("@leased", ProcessingJobStatus.Leased),
            ("@pending", ProcessingJobStatus.Pending),
            ("@owner", leaseOwner),
            ("@lease_exp", PythonTimestamps.Adapter(leaseExpiresAt)),
            ("@now", PythonTimestamps.Adapter(now)),
        };
        var predicate = AdmissionPredicate(admission, lane, parameters);
        var kindsPredicate = KindsPredicate(kinds, parameters);
        var sql = ClaimSqlTemplate
            .Replace("{admission}", predicate, StringComparison.Ordinal)
            .Replace("{kinds}", kindsPredicate, StringComparison.Ordinal);
        var id = Scalar(connection, transaction, sql, [.. parameters]);
        if (id is null or DBNull)
        {
            return null;
        }

        var claimed = Get(connection, transaction, Convert.ToInt64(id, CultureInfo.InvariantCulture))!;
        _metrics.RecordJobEvent(MetricsModule, "started");
        RecordQueueDepth(connection, transaction);
        return claimed;
    }

    /// <summary>
    /// SQL narrowing the claim to work this pass may start. Library ids are integers formatted into the
    /// statement because a variable-length <c>IN</c> list cannot be one bound parameter. The files lane is
    /// gated by each library's pass limit and the resolution budget; the upkeep lane is gated by neither,
    /// only by a library already running its own upkeep (#717).
    /// </summary>
    internal static string AdmissionPredicate(WorkAdmission? admission, WorkLane lane, List<(string Name, object? Value)> parameters)
    {
        if (admission is null)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        var blockedIds = lane == WorkLane.Upkeep ? admission.UpkeepBlockedLibraryIds : admission.BlockedLibraryIds;
        var ids = blockedIds.Distinct().Order().ToList();
        if (ids.Count > 0)
        {
            // A job with no library_id predates libraries or is suite-wide; COALESCE keeps it claimable.
            var rendered = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            clauses.Add($"AND COALESCE(json_extract(payload_json, '$.library_id'), -1) NOT IN ({rendered})");
        }

        if (lane == WorkLane.Files)
        {
            clauses.Add("AND runner_cost <= @available_units");
            parameters.Add(("@available_units", Math.Max(0, admission.AvailableUnits)));
        }

        if (admission.Pause.Paused)
        {
            if (admission.Pause.ScanWhilePaused)
            {
                // substr/= rather than LIKE (#540 item 3): LIKE is case-insensitive and treats '_' as a
                // wildcard, so "processing.watched-folder.remux-scan-dispatch" (hyphens) would match the
                // detection prefix and keep running through a pause.
                var prefixes = new List<string>();
                for (var index = 0; index < WorkAdmissionRules.DetectionJobKindPrefixes.Count; index++)
                {
                    prefixes.Add(PrefixTest(WorkAdmissionRules.DetectionJobKindPrefixes[index], $"@detect_{index}", parameters, negate: false));
                }

                clauses.Add($"AND ({string.Join(" OR ", prefixes)})");
            }
            else
            {
                clauses.Add("AND 1 = 0");
            }
        }

        return "\n    " + string.Join("\n    ", clauses);
    }

    /// <summary>SQL narrowing the claim to kinds this server can run or must refuse.</summary>
    internal static string KindsPredicate(ClaimableKinds? kinds, List<(string Name, object? Value)> parameters)
    {
        if (kinds is null)
        {
            return string.Empty;
        }

        var alternatives = new List<string>();
        var handled = kinds.HandledKinds.Distinct(StringComparer.Ordinal).ToList();
        if (handled.Count > 0)
        {
            var names = new List<string>();
            for (var index = 0; index < handled.Count; index++)
            {
                var name = $"@kind_{index}";
                names.Add(name);
                parameters.Add((name, handled[index]));
            }

            alternatives.Add($"job_kind IN ({string.Join(", ", names)})");
        }

        if (kinds.IncludeRefusedKinds)
        {
            // substr/= rather than LIKE: a prefix test must be exact, and LIKE is case-insensitive and
            // treats '_' as a wildcard.
            alternatives.Add(PrefixTest(JobKindGuard.JobKindPrefix, "@refused_prefix", parameters, negate: true));
            for (var index = 0; index < JobKindGuard.RetiredPrefixes.Count; index++)
            {
                alternatives.Add(PrefixTest(JobKindGuard.RetiredPrefixes[index], $"@retired_{index}", parameters, negate: false));
            }
        }

        return alternatives.Count == 0
            ? "\n    AND 1 = 0"
            : $"\n    AND ({string.Join(" OR ", alternatives)})";
    }

    private static string PrefixTest(string prefix, string name, List<(string Name, object? Value)> parameters, bool negate)
    {
        parameters.Add((name, prefix));
        return $"substr(job_kind, 1, {prefix.Length.ToString(CultureInfo.InvariantCulture)}) {(negate ? "<>" : "=")} {name}";
    }
}
