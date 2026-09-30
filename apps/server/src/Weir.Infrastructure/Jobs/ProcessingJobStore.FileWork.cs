using System.Globalization;
using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>Whether the file lane has anything to do, for whoever needs to know Weir is idle.</summary>
public sealed partial class ProcessingJobStore
{
    /// <summary>
    /// Whether a file pass of <paramref name="fileKinds"/> is running, or is queued and could be leased now. Queued work
    /// held back by a closed schedule window, a switched-off workflow, a pause or the resolution budget does not count: it
    /// is not going to start, whatever else is asked of Weir. Running work counts whatever the schedule says, because a
    /// window closing does not stop a pass already under way. Upkeep (scans, sweeps) is not file work (#875).
    /// </summary>
    /// <remarks>
    /// The queued half asks with the very predicates a worker's claim uses (<see cref="AdmissionPredicate"/>,
    /// <see cref="KindsPredicate"/>), so "could start" here and "would be leased" there cannot drift apart.
    /// </remarks>
    public Task<bool> HasFileWorkAsync(DateTimeOffset now, ClaimableKinds fileKinds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileKinds);
        return ReadAsync(
            (connection, transaction) =>
            {
                var admission = WorkAdmissionReader.Evaluate(connection, transaction, now);
                var parameters = new List<(string Name, object? Value)>
                {
                    ("@leased", ProcessingJobStatus.Leased),
                    ("@pending", ProcessingJobStatus.Pending),
                    ("@now", TimestampColumns.Adapter(now)),
                };
                var kinds = KindsPredicate(fileKinds, parameters);
                var startable = AdmissionPredicate(admission, WorkLane.Files, parameters);
                var sql = $"""
                    SELECT EXISTS (
                      SELECT 1 FROM jobs WHERE status = @leased {kinds}
                      UNION ALL
                      SELECT 1 FROM jobs
                      WHERE status = @pending AND (not_before IS NULL OR julianday(not_before) <= julianday(@now))
                        {startable}{kinds}
                    )
                    """;
                return Convert.ToInt64(Scalar(connection, transaction, sql, [.. parameters]), CultureInfo.InvariantCulture) != 0;
            },
            cancellationToken);
    }
}
