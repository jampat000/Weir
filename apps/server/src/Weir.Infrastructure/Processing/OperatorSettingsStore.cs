using Microsoft.Data.Sqlite;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The singleton <c>operator_settings</c> row.</summary>
/// <remarks>
/// <c>verbose_detection_logging</c> is a column that nothing reads or writes, because nothing acted on the setting.
/// Dropping it would buy nothing and cost a migration, so it keeps its default on a new row; a configuration backup
/// carries it and restores it like any other column.
/// </remarks>
public sealed class OperatorSettingsStore
{
    private const string Columns =
        "max_concurrent_files, runner_capacity, runner_cost_sd, runner_cost_720p, runner_cost_1080p, runner_cost_4k, " +
        "runner_cost_undetermined, work_temp_stale_sweep_enabled, failure_cleanup_enabled, keep_failed_work_files, " +
        "file_log_retention_days, minimum_free_disk_space_mb, movie_schedule_enabled, movie_schedule_hours_limited, movie_schedule_days, " +
        "movie_schedule_start, movie_schedule_end, tv_schedule_enabled, tv_schedule_hours_limited, tv_schedule_days, " +
        "tv_schedule_start, tv_schedule_end, updated_at, runner_budget_enabled, work_temp_stale_sweep_interval_seconds, " +
        "failure_cleanup_interval_seconds, unclaimed_handback_cleanup_enabled, unclaimed_handback_window_days, " +
        "unclaimed_handback_cleanup_interval_seconds";

    public Task<ProcessingOperatorSettingsRecord?> GetAsync(UnitOfWork uow) =>
        uow.QuerySingleAsync($"SELECT {Columns} FROM operator_settings WHERE id = 1", Read);

    /// <summary>The settings row, created with its defaults when it does not exist yet.</summary>
    public async Task<ProcessingOperatorSettingsRecord> EnsureAsync(UnitOfWork uow)
    {
        var row = await GetAsync(uow).ConfigureAwait(false);
        if (row is not null)
        {
            return row;
        }

        await uow.ExecuteAsync(
            "INSERT INTO operator_settings (id, max_concurrent_files, runner_capacity, runner_cost_sd, runner_cost_720p, " +
            "runner_cost_1080p, runner_cost_4k, runner_cost_undetermined, work_temp_stale_sweep_enabled, failure_cleanup_enabled, " +
            "keep_failed_work_files, file_log_retention_days, minimum_free_disk_space_mb, movie_schedule_enabled, movie_schedule_hours_limited, " +
            "movie_schedule_days, movie_schedule_start, movie_schedule_end, tv_schedule_enabled, tv_schedule_hours_limited, " +
            "tv_schedule_days, tv_schedule_start, tv_schedule_end) VALUES (1, 1, 4, 0, 0, 1, 1, 0, 1, 0, 0, 90, 5120, " +
            "1, 0, '', '00:00', '23:59', 1, 0, '', '00:00', '23:59')").ConfigureAwait(false);
        return await GetAsync(uow).ConfigureAwait(false) ?? throw new InvalidOperationException("operator_settings row was not created.");
    }

    public async Task UpdateAsync(UnitOfWork uow, ProcessingOperatorSettingsRecord before, ProcessingOperatorSettingsRecord after)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object?)>();
        void Compare<T>(string column, T left, T right, Func<T, object?> toDb)
        {
            if (!EqualityComparer<T>.Default.Equals(left, right))
            {
                sets.Add($"{column}=${column}");
                parameters.Add(($"${column}", toDb(right)));
            }
        }

        Compare("max_concurrent_files", before.MaxConcurrentFiles, after.MaxConcurrentFiles, v => v);
        Compare("runner_capacity", before.RunnerCapacity, after.RunnerCapacity, v => v);
        Compare("runner_cost_sd", before.RunnerCostSd, after.RunnerCostSd, v => v);
        Compare("runner_cost_720p", before.RunnerCost720P, after.RunnerCost720P, v => v);
        Compare("runner_cost_1080p", before.RunnerCost1080P, after.RunnerCost1080P, v => v);
        Compare("runner_cost_4k", before.RunnerCost4K, after.RunnerCost4K, v => v);
        Compare("runner_cost_undetermined", before.RunnerCostUndetermined, after.RunnerCostUndetermined, v => v);
        Compare("runner_budget_enabled", before.RunnerBudgetEnabled, after.RunnerBudgetEnabled, v => v ? 1 : 0);
        Compare("work_temp_stale_sweep_enabled", before.WorkTempStaleSweepEnabled, after.WorkTempStaleSweepEnabled, v => v ? 1 : 0);
        Compare("failure_cleanup_enabled", before.FailureCleanupEnabled, after.FailureCleanupEnabled, v => v ? 1 : 0);
        Compare("work_temp_stale_sweep_interval_seconds", before.WorkTempStaleSweepIntervalSeconds, after.WorkTempStaleSweepIntervalSeconds, v => v);
        Compare("failure_cleanup_interval_seconds", before.FailureCleanupIntervalSeconds, after.FailureCleanupIntervalSeconds, v => v);
        Compare("unclaimed_handback_cleanup_enabled", before.UnclaimedHandbackCleanupEnabled, after.UnclaimedHandbackCleanupEnabled, v => v ? 1 : 0);
        Compare("unclaimed_handback_window_days", before.UnclaimedHandbackWindowDays, after.UnclaimedHandbackWindowDays, v => v);
        Compare("unclaimed_handback_cleanup_interval_seconds", before.UnclaimedHandbackCleanupIntervalSeconds, after.UnclaimedHandbackCleanupIntervalSeconds, v => v);
        Compare("keep_failed_work_files", before.KeepFailedWorkFiles, after.KeepFailedWorkFiles, v => v ? 1 : 0);
        Compare("file_log_retention_days", before.FileLogRetentionDays, after.FileLogRetentionDays, v => v);
        Compare("minimum_free_disk_space_mb", before.MinimumFreeDiskSpaceMb, after.MinimumFreeDiskSpaceMb, v => v);
        Compare("movie_schedule_enabled", before.MovieScheduleEnabled, after.MovieScheduleEnabled, v => v ? 1 : 0);
        Compare("movie_schedule_hours_limited", before.MovieScheduleHoursLimited, after.MovieScheduleHoursLimited, v => v ? 1 : 0);
        Compare("movie_schedule_days", before.MovieScheduleDays, after.MovieScheduleDays, v => v);
        Compare("movie_schedule_start", before.MovieScheduleStart, after.MovieScheduleStart, v => v);
        Compare("movie_schedule_end", before.MovieScheduleEnd, after.MovieScheduleEnd, v => v);
        Compare("tv_schedule_enabled", before.TvScheduleEnabled, after.TvScheduleEnabled, v => v ? 1 : 0);
        Compare("tv_schedule_hours_limited", before.TvScheduleHoursLimited, after.TvScheduleHoursLimited, v => v ? 1 : 0);
        Compare("tv_schedule_days", before.TvScheduleDays, after.TvScheduleDays, v => v);
        Compare("tv_schedule_start", before.TvScheduleStart, after.TvScheduleStart, v => v);
        Compare("tv_schedule_end", before.TvScheduleEnd, after.TvScheduleEnd, v => v);
        if (sets.Count == 0)
        {
            return;
        }

        sets.Add("updated_at=CURRENT_TIMESTAMP");
        await uow.ExecuteAsync($"UPDATE operator_settings SET {string.Join(", ", sets)} WHERE id = 1", [.. parameters]).ConfigureAwait(false);
    }

    private static ProcessingOperatorSettingsRecord Read(SqliteDataReader reader) => new()
    {
        MaxConcurrentFiles = SqliteValues.GetInt64(reader, 0),
        RunnerCapacity = SqliteValues.GetInt64(reader, 1),
        RunnerCostSd = SqliteValues.GetInt64(reader, 2),
        RunnerCost720P = SqliteValues.GetInt64(reader, 3),
        RunnerCost1080P = SqliteValues.GetInt64(reader, 4),
        RunnerCost4K = SqliteValues.GetInt64(reader, 5),
        RunnerCostUndetermined = SqliteValues.GetInt64(reader, 6),
        WorkTempStaleSweepEnabled = SqliteValues.GetBool(reader, 7),
        FailureCleanupEnabled = SqliteValues.GetBool(reader, 8),
        KeepFailedWorkFiles = SqliteValues.GetBool(reader, 9),
        FileLogRetentionDays = SqliteValues.GetInt64(reader, 10),
        MinimumFreeDiskSpaceMb = SqliteValues.GetInt64(reader, 11),
        MovieScheduleEnabled = SqliteValues.GetBool(reader, 12),
        MovieScheduleHoursLimited = SqliteValues.GetBool(reader, 13),
        MovieScheduleDays = SqliteValues.GetString(reader, 14),
        MovieScheduleStart = SqliteValues.GetString(reader, 15),
        MovieScheduleEnd = SqliteValues.GetString(reader, 16),
        TvScheduleEnabled = SqliteValues.GetBool(reader, 17),
        TvScheduleHoursLimited = SqliteValues.GetBool(reader, 18),
        TvScheduleDays = SqliteValues.GetString(reader, 19),
        TvScheduleStart = SqliteValues.GetString(reader, 20),
        TvScheduleEnd = SqliteValues.GetString(reader, 21),
        UpdatedAt = SqliteValues.GetDateTime(reader, 22),
        RunnerBudgetEnabled = SqliteValues.GetBool(reader, 23),
        WorkTempStaleSweepIntervalSeconds = reader.IsDBNull(24) ? null : reader.GetInt64(24),
        FailureCleanupIntervalSeconds = reader.IsDBNull(25) ? null : reader.GetInt64(25),
        UnclaimedHandbackCleanupEnabled = SqliteValues.GetBool(reader, 26),
        UnclaimedHandbackWindowDays = SqliteValues.GetInt64(reader, 27),
        UnclaimedHandbackCleanupIntervalSeconds = reader.IsDBNull(28) ? null : reader.GetInt64(28),
    };
}
