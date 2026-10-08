using Microsoft.Data.Sqlite;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The <c>libraries</c> row's insert/update SQL, its parameter binding, and reading it back.</summary>
public sealed partial class LibraryStore
{
    private async Task InsertAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        await uow.ExecuteAsync(
            "INSERT INTO libraries (name, enabled, media_type, display_order, watched_folder, work_folder, output_folder, " +
            "media_extensions_csv, exclude_markers_csv, include_patterns_csv, exclude_patterns_csv, min_file_size_mb, max_file_size_mb, " +
            "rejected_file_action, ready_after_seconds, created_after, created_before, modified_after, modified_before, " +
            "exclude_hidden, top_level_only, sidecar_patterns_csv, preserve_original_timestamps, output_collision_policy, " +
            "ffmpeg_strictness, scan_interval_seconds, ignore_size_changes, file_system_events_enabled, skip_access_tests, " +
            "schedule_enabled, schedule_hours_limited, schedule_days, schedule_grid, schedule_start, schedule_end, max_attempts, " +
            "retry_backoff_seconds, retry_execution_failures, retry_preflight_failures, failure_policy, max_concurrent_files, " +
            "priority, rule_set_id, discovered_from_connection_id, discovered_library_key, remux_writer, remove_original_after_success, " +
            "minimum_free_disk_space_mb) " +
            "VALUES (@name, @enabled, @media_type, " +
            "@display_order, @watched_folder, @work_folder, @output_folder, " +
            "@media_extensions_csv, @exclude_markers_csv, @include_patterns_csv, @exclude_patterns_csv, @min_file_size_mb, @max_file_size_mb, " +
            "@rejected_file_action, @ready_after_seconds, @created_after, @created_before, @modified_after, @modified_before, " +
            "@exclude_hidden, @top_level_only, @sidecar_patterns_csv, @preserve_original_timestamps, @output_collision_policy, " +
            "@ffmpeg_strictness, @scan_interval_seconds, @ignore_size_changes, @file_system_events_enabled, @skip_access_tests, " +
            "@schedule_enabled, @schedule_hours_limited, @schedule_days, @schedule_grid, @schedule_start, @schedule_end, @max_attempts, " +
            "@retry_backoff_seconds, @retry_execution_failures, @retry_preflight_failures, @failure_policy, @max_concurrent_files, " +
            "@priority, @rule_set_id, @discovered_from_connection_id, @discovered_library_key, @remux_writer, @remove_original_after_success, " +
            "@minimum_free_disk_space_mb)",
            LibraryParameters(row)).ConfigureAwait(false);
        Changed(uow);
    }

    private async Task UpdateRowAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        await uow.ExecuteAsync(
            "UPDATE libraries SET name=@name, enabled=@enabled, media_type=@media_type, watched_folder=@watched_folder, " +
            "work_folder=@work_folder, output_folder=@output_folder, media_extensions_csv=@media_extensions_csv, " +
            "exclude_markers_csv=@exclude_markers_csv, include_patterns_csv=@include_patterns_csv, exclude_patterns_csv=@exclude_patterns_csv, " +
            "min_file_size_mb=@min_file_size_mb, max_file_size_mb=@max_file_size_mb, rejected_file_action=@rejected_file_action, " +
            "ready_after_seconds=@ready_after_seconds, created_after=@created_after, created_before=@created_before, " +
            "modified_after=@modified_after, modified_before=@modified_before, exclude_hidden=@exclude_hidden, top_level_only=@top_level_only, " +
            "sidecar_patterns_csv=@sidecar_patterns_csv, preserve_original_timestamps=@preserve_original_timestamps, " +
            "output_collision_policy=@output_collision_policy, ffmpeg_strictness=@ffmpeg_strictness, " +
            "scan_interval_seconds=@scan_interval_seconds, " +
            "ignore_size_changes=@ignore_size_changes, file_system_events_enabled=@file_system_events_enabled, skip_access_tests=@skip_access_tests, " +
            "schedule_enabled=@schedule_enabled, schedule_hours_limited=@schedule_hours_limited, schedule_days=@schedule_days, " +
            "schedule_grid=@schedule_grid, schedule_start=@schedule_start, schedule_end=@schedule_end, max_attempts=@max_attempts, " +
            "retry_backoff_seconds=@retry_backoff_seconds, retry_execution_failures=@retry_execution_failures, " +
            "retry_preflight_failures=@retry_preflight_failures, failure_policy=@failure_policy, max_concurrent_files=@max_concurrent_files, " +
            "priority=@priority, rule_set_id=@rule_set_id, remux_writer=@remux_writer, " +
            "remove_original_after_success=@remove_original_after_success, minimum_free_disk_space_mb=@minimum_free_disk_space_mb, " +
            "updated_at=CURRENT_TIMESTAMP WHERE id=@id",
            [.. LibraryParameters(row), ("@id", row.Id)]).ConfigureAwait(false);
        Changed(uow);
    }

    private static (string, object?)[] LibraryParameters(ProcessingLibraryRecord row) =>
    [
        ("@name", row.Name),
        ("@enabled", row.Enabled ? 1 : 0),
        ("@media_type", row.MediaType),
        ("@display_order", row.DisplayOrder),
        ("@watched_folder", row.WatchedFolder),
        ("@work_folder", row.WorkFolder),
        ("@output_folder", row.OutputFolder),
        ("@media_extensions_csv", row.MediaExtensionsCsv),
        ("@exclude_markers_csv", row.ExcludeMarkersCsv),
        ("@include_patterns_csv", row.IncludePatternsCsv),
        ("@exclude_patterns_csv", row.ExcludePatternsCsv),
        ("@min_file_size_mb", row.MinFileSizeMb),
        ("@max_file_size_mb", row.MaxFileSizeMb),
        ("@rejected_file_action", row.RejectedFileAction),
        ("@ready_after_seconds", row.ReadyAfterSeconds),
        ("@created_after", SqliteValues.ToSqlite(row.CreatedAfter)),
        ("@created_before", SqliteValues.ToSqlite(row.CreatedBefore)),
        ("@modified_after", SqliteValues.ToSqlite(row.ModifiedAfter)),
        ("@modified_before", SqliteValues.ToSqlite(row.ModifiedBefore)),
        ("@exclude_hidden", row.ExcludeHidden ? 1 : 0),
        ("@top_level_only", row.TopLevelOnly ? 1 : 0),
        ("@sidecar_patterns_csv", row.SidecarPatternsCsv),
        ("@preserve_original_timestamps", row.PreserveOriginalTimestamps ? 1 : 0),
        ("@output_collision_policy", row.OutputCollisionPolicy),
        ("@ffmpeg_strictness", row.FfmpegStrictness),
        ("@remux_writer", row.RemuxWriter),
        ("@remove_original_after_success", row.RemoveOriginalAfterSuccess ? 1 : 0),
        ("@minimum_free_disk_space_mb", row.MinimumFreeDiskSpaceMb),
        ("@scan_interval_seconds", row.ScanIntervalSeconds),
        ("@ignore_size_changes", row.IgnoreSizeChanges ? 1 : 0),
        ("@file_system_events_enabled", row.FileSystemEventsEnabled ? 1 : 0),
        ("@skip_access_tests", row.SkipAccessTests ? 1 : 0),
        ("@schedule_enabled", row.ScheduleEnabled ? 1 : 0),
        ("@schedule_hours_limited", row.ScheduleHoursLimited ? 1 : 0),
        ("@schedule_days", row.ScheduleDays),
        ("@schedule_grid", row.ScheduleGrid),
        ("@schedule_start", row.ScheduleStart),
        ("@schedule_end", row.ScheduleEnd),
        ("@max_attempts", row.MaxAttempts),
        ("@retry_backoff_seconds", row.RetryBackoffSeconds),
        ("@retry_execution_failures", row.RetryExecutionFailures ? 1 : 0),
        ("@retry_preflight_failures", row.RetryPreflightFailures ? 1 : 0),
        ("@failure_policy", row.FailurePolicy),
        ("@max_concurrent_files", row.MaxConcurrentFiles),
        ("@priority", row.Priority),
        ("@rule_set_id", row.RuleSetId),
        ("@discovered_from_connection_id", row.DiscoveredFromConnectionId),
        ("@discovered_library_key", row.DiscoveredLibraryKey),
    ];

    private static ProcessingLibraryRecord ReadLibrary(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Name = SqliteValues.GetString(reader, 1),
        Enabled = SqliteValues.GetBool(reader, 2),
        MediaType = SqliteValues.GetString(reader, 3),
        DisplayOrder = SqliteValues.GetInt64(reader, 4),
        WatchedFolder = SqliteValues.GetString(reader, 5),
        WorkFolder = SqliteValues.GetString(reader, 6),
        OutputFolder = SqliteValues.GetString(reader, 7),
        MediaExtensionsCsv = SqliteValues.GetString(reader, 8),
        ExcludeMarkersCsv = SqliteValues.GetString(reader, 9),
        IncludePatternsCsv = SqliteValues.GetString(reader, 10),
        ExcludePatternsCsv = SqliteValues.GetString(reader, 11),
        MinFileSizeMb = SqliteValues.GetInt64(reader, 12),
        MaxFileSizeMb = SqliteValues.GetInt64(reader, 13),
        RejectedFileAction = SqliteValues.GetString(reader, 14),
        ReadyAfterSeconds = SqliteValues.GetInt64(reader, 15),
        CreatedAfter = SqliteValues.GetDateTimeOrNull(reader, 16),
        CreatedBefore = SqliteValues.GetDateTimeOrNull(reader, 17),
        ModifiedAfter = SqliteValues.GetDateTimeOrNull(reader, 18),
        ModifiedBefore = SqliteValues.GetDateTimeOrNull(reader, 19),
        ExcludeHidden = SqliteValues.GetBool(reader, 20),
        TopLevelOnly = SqliteValues.GetBool(reader, 21),
        SidecarPatternsCsv = SqliteValues.GetString(reader, 22),
        PreserveOriginalTimestamps = SqliteValues.GetBool(reader, 23),
        OutputCollisionPolicy = SqliteValues.GetString(reader, 24),
        FfmpegStrictness = SqliteValues.GetString(reader, 25),
        ScanIntervalSeconds = SqliteValues.GetInt64(reader, 26),
        IgnoreSizeChanges = SqliteValues.GetBool(reader, 27),
        FileSystemEventsEnabled = SqliteValues.GetBool(reader, 28),
        SkipAccessTests = SqliteValues.GetBool(reader, 29),
        ScheduleEnabled = SqliteValues.GetBool(reader, 30),
        ScheduleHoursLimited = SqliteValues.GetBool(reader, 31),
        ScheduleDays = SqliteValues.GetString(reader, 32),
        ScheduleGrid = SqliteValues.GetString(reader, 33),
        ScheduleStart = SqliteValues.GetString(reader, 34),
        ScheduleEnd = SqliteValues.GetString(reader, 35),
        MaxAttempts = SqliteValues.GetInt64(reader, 36),
        RetryBackoffSeconds = SqliteValues.GetInt64(reader, 37),
        RetryExecutionFailures = SqliteValues.GetBool(reader, 38),
        RetryPreflightFailures = SqliteValues.GetBool(reader, 39),
        FailurePolicy = SqliteValues.GetString(reader, 40),
        MaxConcurrentFiles = SqliteValues.GetInt64(reader, 41),
        Priority = SqliteValues.GetInt64(reader, 42),
        RuleSetId = reader.IsDBNull(43) ? null : reader.GetInt64(43),
        DiscoveredFromConnectionId = reader.IsDBNull(44) ? null : reader.GetInt64(44),
        DiscoveredLibraryKey = SqliteValues.GetStringOrNull(reader, 45),
        CreatedAt = SqliteValues.GetDateTime(reader, 46),
        UpdatedAt = SqliteValues.GetDateTime(reader, 47),
        RemuxWriter = SqliteValues.GetString(reader, 48),
        RemoveOriginalAfterSuccess = SqliteValues.GetBool(reader, 49),
        MinimumFreeDiskSpaceMb = SqliteValues.GetInt64(reader, 50),
    };
}
