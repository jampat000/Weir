using Microsoft.Data.Sqlite;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The singleton <c>operator_settings</c> row (port of <c>operator_settings_service.py</c>).</summary>
/// <remarks>
/// <c>verbose_detection_logging</c> is still a column but nothing reads or writes it: the setting was removed on 23 Sep 2026
/// because nothing ever acted on it. Dropping it would buy nothing and cost a migration, so it stays with its default
/// filling it on a new row; a configuration backup carries it and restores it like any other column.
/// </remarks>
public static class OperatorSettingsStore
{
    private const string Columns =
        "max_concurrent_files, runner_capacity, runner_cost_sd, runner_cost_720p, runner_cost_1080p, runner_cost_4k, " +
        "runner_cost_undetermined, work_temp_stale_sweep_enabled, failure_cleanup_enabled, keep_failed_work_files, " +
        "file_log_retention_days, min_file_age_seconds, min_input_file_size_mb, " +
        "minimum_free_disk_space_mb, movie_schedule_enabled, movie_schedule_hours_limited, movie_schedule_days, " +
        "movie_schedule_start, movie_schedule_end, tv_schedule_enabled, tv_schedule_hours_limited, tv_schedule_days, " +
        "tv_schedule_start, tv_schedule_end, updated_at, runner_budget_enabled, work_temp_stale_sweep_interval_seconds, " +
        "failure_cleanup_interval_seconds, unclaimed_handback_cleanup_enabled, unclaimed_handback_window_days, " +
        "unclaimed_handback_cleanup_interval_seconds";

    public static Task<ProcessingOperatorSettingsRecord?> GetAsync(UnitOfWork uow) =>
        uow.QuerySingleAsync($"SELECT {Columns} FROM operator_settings WHERE id = 1", Read);

    /// <summary><c>ensure_operator_settings_row</c>.</summary>
    public static async Task<ProcessingOperatorSettingsRecord> EnsureAsync(UnitOfWork uow)
    {
        var row = await GetAsync(uow).ConfigureAwait(false);
        if (row is not null)
        {
            return row;
        }

        await uow.ExecuteAsync(
            "INSERT INTO operator_settings (id, max_concurrent_files, runner_capacity, runner_cost_sd, runner_cost_720p, " +
            "runner_cost_1080p, runner_cost_4k, runner_cost_undetermined, work_temp_stale_sweep_enabled, failure_cleanup_enabled, " +
            "keep_failed_work_files, file_log_retention_days, min_file_age_seconds, " +
            "min_input_file_size_mb, minimum_free_disk_space_mb, movie_schedule_enabled, movie_schedule_hours_limited, " +
            "movie_schedule_days, movie_schedule_start, movie_schedule_end, tv_schedule_enabled, tv_schedule_hours_limited, " +
            "tv_schedule_days, tv_schedule_start, tv_schedule_end) VALUES (1, 1, 4, 0, 0, 1, 1, 0, 1, 0, 0, 90, 60, 50, 5120, " +
            "1, 0, '', '00:00', '23:59', 1, 0, '', '00:00', '23:59')").ConfigureAwait(false);
        return await GetAsync(uow).ConfigureAwait(false) ?? throw new InvalidOperationException("operator_settings row was not created.");
    }

    public static async Task UpdateAsync(UnitOfWork uow, ProcessingOperatorSettingsRecord before, ProcessingOperatorSettingsRecord after)
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
        Compare("min_file_age_seconds", before.MinFileAgeSeconds, after.MinFileAgeSeconds, v => v);
        Compare("min_input_file_size_mb", before.ProcessingMinInputFileSizeMb, after.ProcessingMinInputFileSizeMb, v => v);
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
        MinFileAgeSeconds = SqliteValues.GetInt64(reader, 11),
        ProcessingMinInputFileSizeMb = SqliteValues.GetInt64(reader, 12),
        MinimumFreeDiskSpaceMb = SqliteValues.GetInt64(reader, 13),
        MovieScheduleEnabled = SqliteValues.GetBool(reader, 14),
        MovieScheduleHoursLimited = SqliteValues.GetBool(reader, 15),
        MovieScheduleDays = SqliteValues.GetString(reader, 16),
        MovieScheduleStart = SqliteValues.GetString(reader, 17),
        MovieScheduleEnd = SqliteValues.GetString(reader, 18),
        TvScheduleEnabled = SqliteValues.GetBool(reader, 19),
        TvScheduleHoursLimited = SqliteValues.GetBool(reader, 20),
        TvScheduleDays = SqliteValues.GetString(reader, 21),
        TvScheduleStart = SqliteValues.GetString(reader, 22),
        TvScheduleEnd = SqliteValues.GetString(reader, 23),
        UpdatedAt = SqliteValues.GetDateTime(reader, 24),
        RunnerBudgetEnabled = SqliteValues.GetBool(reader, 25),
        WorkTempStaleSweepIntervalSeconds = reader.IsDBNull(26) ? null : reader.GetInt64(26),
        FailureCleanupIntervalSeconds = reader.IsDBNull(27) ? null : reader.GetInt64(27),
        UnclaimedHandbackCleanupEnabled = SqliteValues.GetBool(reader, 28),
        UnclaimedHandbackWindowDays = SqliteValues.GetInt64(reader, 29),
        UnclaimedHandbackCleanupIntervalSeconds = reader.IsDBNull(30) ? null : reader.GetInt64(30),
    };
}
