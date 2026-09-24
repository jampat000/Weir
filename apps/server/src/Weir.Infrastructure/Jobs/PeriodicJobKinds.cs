namespace Weir.Infrastructure.Jobs;

/// <summary>Job kinds and dedupe keys of the periodic families.</summary>
public static class PeriodicJobKinds
{
    public const string WorkTempStaleSweep = "processing.work_temp_stale_sweep.v1";
    public const string WorkTempStaleSweepDedupeKeyMovie = "processing.work_temp_stale_sweep:v1:movie";
    public const string WorkTempStaleSweepDedupeKeyTv = "processing.work_temp_stale_sweep:v1:tv";
    public const string MovieFailureCleanupSweep = "processing.movie_failure_cleanup_sweep.v1";
    public const string TvFailureCleanupSweep = "processing.tv_failure_cleanup_sweep.v1";
    public const string MovieFailureCleanupSweepDedupeKey = "processing.movie_failure_cleanup_sweep:v1";
    public const string TvFailureCleanupSweepDedupeKey = "processing.tv_failure_cleanup_sweep:v1";

    /// <summary>Removes hand-back copies nobody claimed (#652).</summary>
    public const string UnclaimedHandbackCleanup = "processing.unclaimed_handback_cleanup.v1";
    public const string UnclaimedHandbackCleanupDedupeKeyMovie = "processing.unclaimed_handback_cleanup:v1:movie";
    public const string UnclaimedHandbackCleanupDedupeKeyTv = "processing.unclaimed_handback_cleanup:v1:tv";
}
