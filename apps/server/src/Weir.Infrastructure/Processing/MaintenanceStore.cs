using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>One maintenance family's current state.</summary>
public sealed record MaintenanceFamilyState(
    string Family,
    bool Enabled,
    string Description,
    long Pending,
    long Running,
    Timestamp? LastCompletedAt,
    Timestamp? LastFailedAt,
    string? LastError);

/// <summary>The Processing maintenance jobs' read model and manual triggers.</summary>
public sealed class MaintenanceStore
{
    private static readonly Dictionary<string, IReadOnlyList<string>> FamilyJobKinds = new(StringComparer.Ordinal)
    {
        ["work_temp_stale_sweep"] = [PeriodicJobKinds.WorkTempStaleSweep],
        ["unclaimed_handbacks"] = [PeriodicJobKinds.UnclaimedHandbackCleanup],
    };

    private static readonly Dictionary<string, string> FamilyDescriptions = new(StringComparer.Ordinal)
    {
        ["work_temp_stale_sweep"] = "Deletes half-written copies Weir left in its work folders once they have sat for a day, including a failed copy you asked Weir to keep. Never touches a file being written.",
        ["unclaimed_handbacks"] = "Deletes Weir's own cleaned copy from a hand-back folder when no media manager imported it in time. Only a copy that is still exactly as Weir wrote it; never a download. It stays off until you switch it on.",
    };

    /// <summary>The families Settings › Cleanup lists, in its order.</summary>
    public IReadOnlyList<string> Families { get; } = ["work_temp_stale_sweep", "unclaimed_handbacks"];

    /// <summary>The job kinds a family's timers queue, for asking the clock when it next runs.</summary>
    public IReadOnlyList<string> JobKindsFor(string family) => FamilyJobKinds[family];

    public async Task<MaintenanceFamilyState> StateForAsync(UnitOfWork uow, string family, bool enabled)
    {
        var kinds = FamilyJobKinds[family];
        var placeholders = string.Join(",", kinds.Select((_, i) => $"@kind{i}"));
        var parameters = kinds.Select((k, i) => ($"@kind{i}", (object?)k)).ToArray();
        var rows = await uow.QueryAsync(
            $"SELECT status, updated_at, last_error FROM jobs WHERE job_kind IN ({placeholders}) ORDER BY id DESC",
            reader => (Status: reader.GetString(0), UpdatedAt: SqliteValues.GetDateTime(reader, 1), LastError: SqliteValues.GetStringOrNull(reader, 2)),
            parameters).ConfigureAwait(false);

        var pending = rows.Count(r => r.Status == "pending");
        var running = rows.Count(r => r.Status == "leased");
        var completed = rows.FirstOrDefault(r => r.Status == "completed");
        var failed = rows.FirstOrDefault(r => r.Status == "failed");

        return new MaintenanceFamilyState(
            family, enabled, FamilyDescriptions[family], pending, running,
            completed.Status is null ? null : completed.UpdatedAt,
            failed.Status is null ? null : failed.UpdatedAt,
            failed.Status is null ? null : failed.LastError);
    }

    /// <summary>Queues the stale work-temp sweep now: single-flight per scope, ignores the schedule toggle.</summary>
    public Task EnqueueWorkTempStaleSweepAsync(ProcessingJobStore jobStore, string mediaScope, string trigger)
    {
        var scope = mediaScope == "tv" ? "tv" : "movie";
        var dedupe = scope == "tv" ? PeriodicJobKinds.WorkTempStaleSweepDedupeKeyTv : PeriodicJobKinds.WorkTempStaleSweepDedupeKeyMovie;
        var payload = WireJsonWriter.Dumps(new WireObject().Set("media_scope", scope).Set("trigger", trigger), WireJsonFormat.Compact);
        return jobStore.EnqueueOrGetAsync(dedupe, PeriodicJobKinds.WorkTempStaleSweep, payload);
    }

    /// <summary>The unclaimed hand-back cleanup now (#652): single-flight per scope, ignores the schedule toggle like the others.</summary>
    public Task EnqueueUnclaimedHandbackCleanupAsync(ProcessingJobStore jobStore, string mediaScope, string trigger)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        var scope = mediaScope == "tv" ? "tv" : "movie";
        var dedupe = scope == "tv" ? PeriodicJobKinds.UnclaimedHandbackCleanupDedupeKeyTv : PeriodicJobKinds.UnclaimedHandbackCleanupDedupeKeyMovie;
        var payload = WireJsonWriter.Dumps(new WireObject().Set("media_scope", scope).Set("trigger", trigger), WireJsonFormat.Compact);
        return jobStore.EnqueueOrGetAsync(dedupe, PeriodicJobKinds.UnclaimedHandbackCleanup, payload);
    }
}
