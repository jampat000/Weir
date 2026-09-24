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
            "rejected_file_action, min_file_age_seconds, created_after, created_before, modified_after, modified_before, " +
            "exclude_hidden, top_level_only, sidecar_patterns_csv, preserve_original_timestamps, output_collision_policy, " +
            "hardware_decode_mode, hardware_device, hardware_disabled_vendors_csv, ffmpeg_strictness, scan_interval_seconds, " +
            "hold_minutes, file_detection_interval_seconds, ignore_size_changes, file_system_events_enabled, skip_access_tests, " +
            "schedule_enabled, schedule_hours_limited, schedule_days, schedule_grid, schedule_start, schedule_end, max_attempts, " +
            "retry_backoff_seconds, retry_execution_failures, retry_preflight_failures, failure_policy, max_concurrent_files, " +
            "priority, rule_set_id, discovered_from_connection_id, discovered_library_key, remux_writer, rewrite_with_ffmpeg, remove_original_after_success) " +
            "VALUES (@name, @enabled, @media_type, " +
            "@display_order, @watched_folder, @work_folder, @output_folder, " +
            "@media_extensions_csv, @exclude_markers_csv, @include_patterns_csv, @exclude_patterns_csv, @min_file_size_mb, @max_file_size_mb, " +
            "@rejected_file_action, @min_file_age_seconds, @created_after, @created_before, @modified_after, @modified_before, " +
            "@exclude_hidden, @top_level_only, @sidecar_patterns_csv, @preserve_original_timestamps, @output_collision_policy, " +
            "@hardware_decode_mode, @hardware_device, @hardware_disabled_vendors_csv, @ffmpeg_strictness, @scan_interval_seconds, " +
            "@hold_minutes, @file_detection_interval_seconds, @ignore_size_changes, @file_system_events_enabled, @skip_access_tests, " +
            "@schedule_enabled, @schedule_hours_limited, @schedule_days, @schedule_grid, @schedule_start, @schedule_end, @max_attempts, " +
            "@retry_backoff_seconds, @retry_execution_failures, @retry_preflight_failures, @failure_policy, @max_concurrent_files, " +
            "@priority, @rule_set_id, @discovered_from_connection_id, @discovered_library_key, @remux_writer, @rewrite_with_ffmpeg, @remove_original_after_success)",
            LibraryParameters(row)).ConfigureAwait(false);
    }

    private async Task UpdateRowAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        await uow.ExecuteAsync(
            "UPDATE libraries SET name=@name, enabled=@enabled, media_type=@media_type, watched_folder=@watched_folder, " +
            "work_folder=@work_folder, output_folder=@output_folder, media_extensions_csv=@media_extensions_csv, " +
            "exclude_markers_csv=@exclude_markers_csv, include_patterns_csv=@include_patterns_csv, exclude_patterns_csv=@exclude_patterns_csv, " +
            "min_file_size_mb=@min_file_size_mb, max_file_size_mb=@max_file_size_mb, rejected_file_action=@rejected_file_action, " +
            "min_file_age_seconds=@min_file_age_seconds, created_after=@created_after, created_before=@created_before, " +
            "modified_after=@modified_after, modified_before=@modified_before, exclude_hidden=@exclude_hidden, top_level_only=@top_level_only, " +
            "sidecar_patterns_csv=@sidecar_patterns_csv, preserve_original_timestamps=@preserve_original_timestamps, " +
            "output_collision_policy=@output_collision_policy, hardware_decode_mode=@hardware_decode_mode, hardware_device=@hardware_device, " +
            "hardware_disabled_vendors_csv=@hardware_disabled_vendors_csv, ffmpeg_strictness=@ffmpeg_strictness, " +
            "scan_interval_seconds=@scan_interval_seconds, hold_minutes=@hold_minutes, file_detection_interval_seconds=@file_detection_interval_seconds, " +
            "ignore_size_changes=@ignore_size_changes, file_system_events_enabled=@file_system_events_enabled, skip_access_tests=@skip_access_tests, " +
            "schedule_enabled=@schedule_enabled, schedule_hours_limited=@schedule_hours_limited, schedule_days=@schedule_days, " +
            "schedule_grid=@schedule_grid, schedule_start=@schedule_start, schedule_end=@schedule_end, max_attempts=@max_attempts, " +
            "retry_backoff_seconds=@retry_backoff_seconds, retry_execution_failures=@retry_execution_failures, " +
            "retry_preflight_failures=@retry_preflight_failures, failure_policy=@failure_policy, max_concurrent_files=@max_concurrent_files, " +
            "priority=@priority, rule_set_id=@rule_set_id, remux_writer=@remux_writer, rewrite_with_ffmpeg=@rewrite_with_ffmpeg, " +
            "remove_original_after_success=@remove_original_after_success, " +
            "updated_at=CURRENT_TIMESTAMP WHERE id=@id",
            [.. LibraryParameters(row), ("@id", row.Id)]).ConfigureAwait(false);
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
        ("@min_file_age_seconds", row.MinFileAgeSeconds),
        ("@created_after", SqliteValues.ToSqlite(row.CreatedAfter)),
        ("@created_before", SqliteValues.ToSqlite(row.CreatedBefore)),
        ("@modified_after", SqliteValues.ToSqlite(row.ModifiedAfter)),
        ("@modified_before", SqliteValues.ToSqlite(row.ModifiedBefore)),
        ("@exclude_hidden", row.ExcludeHidden ? 1 : 0),
        ("@top_level_only", row.TopLevelOnly ? 1 : 0),
        ("@sidecar_patterns_csv", row.SidecarPatternsCsv),
        ("@preserve_original_timestamps", row.PreserveOriginalTimestamps ? 1 : 0),
        ("@output_collision_policy", row.OutputCollisionPolicy),
        ("@hardware_decode_mode", row.HardwareDecodeMode),
        ("@hardware_device", row.HardwareDevice),
        ("@hardware_disabled_vendors_csv", row.HardwareDisabledVendorsCsv),
        ("@ffmpeg_strictness", row.FfmpegStrictness),
        ("@remux_writer", row.RemuxWriter),
        ("@rewrite_with_ffmpeg", row.RewriteWithFfmpeg ? 1 : 0),
        ("@remove_original_after_success", row.RemoveOriginalAfterSuccess ? 1 : 0),
        ("@scan_interval_seconds", row.ScanIntervalSeconds),
        ("@hold_minutes", row.HoldMinutes),
        ("@file_detection_interval_seconds", row.FileDetectionIntervalSeconds),
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
        MinFileAgeSeconds = SqliteValues.GetInt64(reader, 15),
        CreatedAfter = SqliteValues.GetDateTimeOrNull(reader, 16),
        CreatedBefore = SqliteValues.GetDateTimeOrNull(reader, 17),
        ModifiedAfter = SqliteValues.GetDateTimeOrNull(reader, 18),
        ModifiedBefore = SqliteValues.GetDateTimeOrNull(reader, 19),
        ExcludeHidden = SqliteValues.GetBool(reader, 20),
        TopLevelOnly = SqliteValues.GetBool(reader, 21),
        SidecarPatternsCsv = SqliteValues.GetString(reader, 22),
        PreserveOriginalTimestamps = SqliteValues.GetBool(reader, 23),
        OutputCollisionPolicy = SqliteValues.GetString(reader, 24),
        HardwareDecodeMode = SqliteValues.GetString(reader, 25),
        HardwareDevice = SqliteValues.GetString(reader, 26),
        HardwareDisabledVendorsCsv = SqliteValues.GetString(reader, 27),
        FfmpegStrictness = SqliteValues.GetString(reader, 28),
        ScanIntervalSeconds = SqliteValues.GetInt64(reader, 29),
        HoldMinutes = SqliteValues.GetInt64(reader, 30),
        FileDetectionIntervalSeconds = SqliteValues.GetInt64(reader, 31),
        IgnoreSizeChanges = SqliteValues.GetBool(reader, 32),
        FileSystemEventsEnabled = SqliteValues.GetBool(reader, 33),
        SkipAccessTests = SqliteValues.GetBool(reader, 34),
        ScheduleEnabled = SqliteValues.GetBool(reader, 35),
        ScheduleHoursLimited = SqliteValues.GetBool(reader, 36),
        ScheduleDays = SqliteValues.GetString(reader, 37),
        ScheduleGrid = SqliteValues.GetString(reader, 38),
        ScheduleStart = SqliteValues.GetString(reader, 39),
        ScheduleEnd = SqliteValues.GetString(reader, 40),
        MaxAttempts = SqliteValues.GetInt64(reader, 41),
        RetryBackoffSeconds = SqliteValues.GetInt64(reader, 42),
        RetryExecutionFailures = SqliteValues.GetBool(reader, 43),
        RetryPreflightFailures = SqliteValues.GetBool(reader, 44),
        FailurePolicy = SqliteValues.GetString(reader, 45),
        MaxConcurrentFiles = SqliteValues.GetInt64(reader, 46),
        Priority = SqliteValues.GetInt64(reader, 47),
        RuleSetId = reader.IsDBNull(48) ? null : reader.GetInt64(48),
        DiscoveredFromConnectionId = reader.IsDBNull(49) ? null : reader.GetInt64(49),
        DiscoveredLibraryKey = SqliteValues.GetStringOrNull(reader, 50),
        CreatedAt = SqliteValues.GetDateTime(reader, 51),
        UpdatedAt = SqliteValues.GetDateTime(reader, 52),
        RemuxWriter = SqliteValues.GetString(reader, 53),
        RewriteWithFfmpeg = SqliteValues.GetBool(reader, 54),
        RemoveOriginalAfterSuccess = SqliteValues.GetBool(reader, 55),
    };
}
