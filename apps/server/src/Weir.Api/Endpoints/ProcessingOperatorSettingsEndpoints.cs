using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Validation;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>Reading and updating the operator-editable processing automation settings.</summary>
public static class ProcessingOperatorSettingsEndpoints
{
    public static IEndpointRouteBuilder MapProcessingOperatorSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<ProcessingOperatorSettingsEndpointHandlers>();
        endpoints.MapV1("GET", "/processing/operator-settings", handlers.GetOperatorSettingsAsync);
        endpoints.MapV1("PUT", "/processing/operator-settings", handlers.PutOperatorSettingsAsync);
        return endpoints;
    }
}

/// <summary>Handlers for <see cref="ProcessingOperatorSettingsEndpoints"/>, constructor-injected with the stores they need.</summary>
internal sealed class ProcessingOperatorSettingsEndpointHandlers
{
    private readonly OperatorSettingsStore _operatorSettings;
    private readonly ScanSettingsChanges _scanSettingsChanges;
    private readonly SuiteSettingsStore _suiteSettings;

    public ProcessingOperatorSettingsEndpointHandlers(
        OperatorSettingsStore operatorSettings, ScanSettingsChanges scanSettingsChanges, SuiteSettingsStore suiteSettings)
    {
        _operatorSettings = operatorSettings ?? throw new ArgumentNullException(nameof(operatorSettings));
        _scanSettingsChanges = scanSettingsChanges ?? throw new ArgumentNullException(nameof(scanSettingsChanges));
        _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));
    }

    private static WireObject OperatorSettingsOut(ProcessingOperatorSettingsRecord row, string timezone) => new WireObject()
        .Set("max_concurrent_files", OperatorSettingsRules.ClampMaxConcurrentFiles(row.MaxConcurrentFiles))
        .Set("runner_capacity", OperatorSettingsRules.ClampRunnerCapacity(row.RunnerCapacity))
        .Set("runner_cost_sd", OperatorSettingsRules.ClampRunnerCost(row.RunnerCostSd))
        .Set("runner_cost_720p", OperatorSettingsRules.ClampRunnerCost(row.RunnerCost720P))
        .Set("runner_cost_1080p", OperatorSettingsRules.ClampRunnerCost(row.RunnerCost1080P))
        .Set("runner_cost_4k", OperatorSettingsRules.ClampRunnerCost(row.RunnerCost4K))
        .Set("runner_budget_enabled", row.RunnerBudgetEnabled)
        .Set("work_temp_stale_sweep_enabled", row.WorkTempStaleSweepEnabled)
        .Set("failure_cleanup_enabled", row.FailureCleanupEnabled)
        // Null: the interval the environment gives. Settings › Cleanup shows the one in force from /processing/maintenance.
        .Set("work_temp_stale_sweep_interval_seconds", row.WorkTempStaleSweepIntervalSeconds is { } sweepEvery ? WireValue.Of(sweepEvery) : WireValue.Null)
        .Set("failure_cleanup_interval_seconds", row.FailureCleanupIntervalSeconds is { } cleanupEvery ? WireValue.Of(cleanupEvery) : WireValue.Null)
        // #652: Settings › Cleanup › Unclaimed hand-backs. Off until a person switches it on; a null interval is six hours.
        .Set("unclaimed_handback_cleanup_enabled", row.UnclaimedHandbackCleanupEnabled)
        .Set("unclaimed_handback_window_days", OperatorSettingsRules.ClampUnclaimedHandbackWindowDays(row.UnclaimedHandbackWindowDays))
        .Set("unclaimed_handback_cleanup_interval_seconds", row.UnclaimedHandbackCleanupIntervalSeconds is { } unclaimedEvery ? WireValue.Of(unclaimedEvery) : WireValue.Null)
        .Set("keep_failed_work_files", row.KeepFailedWorkFiles)
        .Set("file_log_retention_days", OperatorSettingsRules.ClampFileLogRetentionDays(row.FileLogRetentionDays))
        .Set("runner_cost_undetermined", OperatorSettingsRules.ClampRunnerCost(row.RunnerCostUndetermined))
        .Set("min_file_age_seconds", OperatorSettingsRules.ClampMinFileAgeSeconds(row.MinFileAgeSeconds))
        .Set("min_input_file_size_mb", OperatorSettingsRules.ClampSizeMb(row.ProcessingMinInputFileSizeMb))
        .Set("minimum_free_disk_space_mb", OperatorSettingsRules.ClampSizeMb(row.MinimumFreeDiskSpaceMb))
        .Set("movie_schedule_enabled", row.MovieScheduleEnabled)
        .Set("movie_schedule_hours_limited", row.MovieScheduleHoursLimited)
        .Set("movie_schedule_days", row.MovieScheduleDays)
        .Set("movie_schedule_start", row.MovieScheduleStart)
        .Set("movie_schedule_end", row.MovieScheduleEnd)
        .Set("tv_schedule_enabled", row.TvScheduleEnabled)
        .Set("tv_schedule_hours_limited", row.TvScheduleHoursLimited)
        .Set("tv_schedule_days", row.TvScheduleDays)
        .Set("tv_schedule_start", row.TvScheduleStart)
        .Set("tv_schedule_end", row.TvScheduleEnd)
        .Set("schedule_timezone", timezone)
        .Set("updated_at", row.UpdatedAt.IsoFormat());

    public async Task<ApiResult> GetOperatorSettingsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await _operatorSettings.EnsureAsync(uow).ConfigureAwait(false);
        var suite = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        return ApiRoutes.Ok(OperatorSettingsOut(row, string.IsNullOrWhiteSpace(suite.AppTimezone) ? "UTC" : suite.AppTimezone.Trim()));
    }

    public async Task<ApiResult> PutOperatorSettingsAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var maxConcurrentFiles = model.OptionalInt("max_concurrent_files", ge: 1, le: OperatorSettingsRules.MaxFilesAtOnce);
        var runnerCapacity = model.OptionalInt("runner_capacity", ge: 1, le: 64);
        var runnerCostSd = model.OptionalInt("runner_cost_sd", ge: 0, le: 64);
        var runnerCost720P = model.OptionalInt("runner_cost_720p", ge: 0, le: 64);
        var runnerCost1080P = model.OptionalInt("runner_cost_1080p", ge: 0, le: 64);
        var runnerCost4K = model.OptionalInt("runner_cost_4k", ge: 0, le: 64);
        var runnerCostUndetermined = model.OptionalInt("runner_cost_undetermined", ge: 0, le: 64);
        var runnerBudgetEnabled = model.OptionalBool("runner_budget_enabled");
        var workTempStaleSweepEnabled = model.OptionalBool("work_temp_stale_sweep_enabled");
        var failureCleanupEnabled = model.OptionalBool("failure_cleanup_enabled");
        var workTempStaleSweepIntervalSeconds = model.OptionalInt(
            "work_temp_stale_sweep_interval_seconds", ge: OperatorSettingsRules.MinCleanupIntervalSeconds, le: OperatorSettingsRules.MaxCleanupIntervalSeconds);
        var failureCleanupIntervalSeconds = model.OptionalInt(
            "failure_cleanup_interval_seconds", ge: OperatorSettingsRules.MinCleanupIntervalSeconds, le: OperatorSettingsRules.MaxCleanupIntervalSeconds);
        var unclaimedHandbackCleanupEnabled = model.OptionalBool("unclaimed_handback_cleanup_enabled");
        var unclaimedHandbackWindowDays = model.OptionalInt(
            "unclaimed_handback_window_days", ge: HandbackRules.MinUnclaimedWindowDays, le: HandbackRules.MaxUnclaimedWindowDays);
        var unclaimedHandbackCleanupIntervalSeconds = model.OptionalInt(
            "unclaimed_handback_cleanup_interval_seconds", ge: OperatorSettingsRules.MinCleanupIntervalSeconds, le: OperatorSettingsRules.MaxCleanupIntervalSeconds);
        var keepFailedWorkFiles = model.OptionalBool("keep_failed_work_files");
        var fileLogRetentionDays = model.OptionalInt("file_log_retention_days", ge: 0, le: 3650);
        // Retired setting (nothing acts on it). Read and ignored so an older client that sends it is not refused:
        // the body forbids fields it does not know.
        var retiredVerboseDetectionLogging = model.OptionalBool("verbose_detection_logging");
        var minFileAgeSeconds = model.OptionalInt("min_file_age_seconds", ge: 0, le: 7 * 24 * 3600);
        var processingMinInputFileSizeMb = model.OptionalInt("min_input_file_size_mb", ge: 0, le: 1024 * 1024);
        var minimumFreeDiskSpaceMb = model.OptionalInt("minimum_free_disk_space_mb", ge: 0, le: 1024 * 1024);
        var movieScheduleEnabled = model.OptionalBool("movie_schedule_enabled");
        var movieScheduleHoursLimited = model.OptionalBool("movie_schedule_hours_limited");
        var movieScheduleDays = model.OptionalStr("movie_schedule_days", maxLength: 2000);
        var movieScheduleStart = model.OptionalStr("movie_schedule_start", maxLength: 5);
        var movieScheduleEnd = model.OptionalStr("movie_schedule_end", maxLength: 5);
        var tvScheduleEnabled = model.OptionalBool("tv_schedule_enabled");
        var tvScheduleHoursLimited = model.OptionalBool("tv_schedule_hours_limited");
        var tvScheduleDays = model.OptionalStr("tv_schedule_days", maxLength: 2000);
        var tvScheduleStart = model.OptionalStr("tv_schedule_start", maxLength: 5);
        var tvScheduleEnd = model.OptionalStr("tv_schedule_end", maxLength: 5);
        model.Finish(ExtraFields.Forbid);

        var movieGroupCount = new bool?[] { movieScheduleEnabled, movieScheduleHoursLimited }.Count(v => v is not null) +
                               new[] { movieScheduleDays, movieScheduleStart, movieScheduleEnd }.Count(v => v is not null);
        if (movieGroupCount != 0 && movieGroupCount != 5)
        {
            issues.Add(new ValidationIssue("value_error", ["body"], "Value error, Movie schedule fields must all be omitted or all provided together.", WireValue.Null));
        }

        var tvGroupCount = new bool?[] { tvScheduleEnabled, tvScheduleHoursLimited }.Count(v => v is not null) +
                            new[] { tvScheduleDays, tvScheduleStart, tvScheduleEnd }.Count(v => v is not null);
        if (tvGroupCount != 0 && tvGroupCount != 5)
        {
            issues.Add(new ValidationIssue("value_error", ["body"], "Value error, TV schedule fields must all be omitted or all provided together.", WireValue.Null));
        }

        var hasProcessField = maxConcurrentFiles is not null || runnerCapacity is not null || runnerCostSd is not null ||
                               runnerCost720P is not null || runnerCost1080P is not null || runnerCost4K is not null ||
                               runnerCostUndetermined is not null || runnerBudgetEnabled is not null || workTempStaleSweepEnabled is not null || failureCleanupEnabled is not null ||
                               workTempStaleSweepIntervalSeconds is not null || failureCleanupIntervalSeconds is not null ||
                               unclaimedHandbackCleanupEnabled is not null || unclaimedHandbackWindowDays is not null ||
                               unclaimedHandbackCleanupIntervalSeconds is not null ||
                               keepFailedWorkFiles is not null || fileLogRetentionDays is not null || retiredVerboseDetectionLogging is not null ||
                               minFileAgeSeconds is not null || processingMinInputFileSizeMb is not null || minimumFreeDiskSpaceMb is not null;
        if (!hasProcessField && movieScheduleEnabled is null && tvScheduleEnabled is null)
        {
            issues.Add(new ValidationIssue("value_error", ["body"], "Value error, No operator settings fields to update.", WireValue.Null));
        }

        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var before = await _operatorSettings.EnsureAsync(uow).ConfigureAwait(false);
        var after = before;
        if (maxConcurrentFiles is { } m)
        {
            after = after with { MaxConcurrentFiles = OperatorSettingsRules.ClampMaxConcurrentFiles(m) };
        }

        if (runnerBudgetEnabled is { } budgetOn)
        {
            after = after with { RunnerBudgetEnabled = budgetOn };
        }

        if (runnerCapacity is { } rc)
        {
            after = after with { RunnerCapacity = Math.Clamp(rc, 0, 64) };
        }

        if (runnerCostSd is { } v1)
        {
            after = after with { RunnerCostSd = Math.Clamp(v1, 0, 64) };
        }

        if (runnerCost720P is { } v2)
        {
            after = after with { RunnerCost720P = Math.Clamp(v2, 0, 64) };
        }

        if (runnerCost1080P is { } v3)
        {
            after = after with { RunnerCost1080P = Math.Clamp(v3, 0, 64) };
        }

        if (runnerCost4K is { } v4)
        {
            after = after with { RunnerCost4K = Math.Clamp(v4, 0, 64) };
        }

        if (runnerCostUndetermined is { } v5)
        {
            after = after with { RunnerCostUndetermined = Math.Clamp(v5, 0, 64) };
        }

        if (fileLogRetentionDays is { } fl)
        {
            after = after with { FileLogRetentionDays = OperatorSettingsRules.ClampFileLogRetentionDays(fl) };
        }

        if (workTempStaleSweepEnabled is { } wt)
        {
            after = after with { WorkTempStaleSweepEnabled = wt };
        }

        if (failureCleanupEnabled is { } fc)
        {
            after = after with { FailureCleanupEnabled = fc };
        }

        if (workTempStaleSweepIntervalSeconds is { } sweepEvery)
        {
            after = after with { WorkTempStaleSweepIntervalSeconds = sweepEvery };
        }

        if (failureCleanupIntervalSeconds is { } cleanupEvery)
        {
            after = after with { FailureCleanupIntervalSeconds = cleanupEvery };
        }

        if (unclaimedHandbackCleanupEnabled is { } unclaimedOn)
        {
            after = after with { UnclaimedHandbackCleanupEnabled = unclaimedOn };
        }

        if (unclaimedHandbackWindowDays is { } unclaimedDays)
        {
            after = after with { UnclaimedHandbackWindowDays = OperatorSettingsRules.ClampUnclaimedHandbackWindowDays(unclaimedDays) };
        }

        if (unclaimedHandbackCleanupIntervalSeconds is { } unclaimedEvery)
        {
            after = after with { UnclaimedHandbackCleanupIntervalSeconds = unclaimedEvery };
        }

        if (keepFailedWorkFiles is { } kf)
        {
            after = after with { KeepFailedWorkFiles = kf };
        }

        if (minFileAgeSeconds is { } mfa)
        {
            after = after with { MinFileAgeSeconds = OperatorSettingsRules.ClampMinFileAgeSeconds(mfa) };
        }

        if (processingMinInputFileSizeMb is { } rmin)
        {
            after = after with { ProcessingMinInputFileSizeMb = OperatorSettingsRules.ClampSizeMb(rmin) };
        }

        if (minimumFreeDiskSpaceMb is { } mfd)
        {
            after = after with { MinimumFreeDiskSpaceMb = OperatorSettingsRules.ClampSizeMb(mfd) };
        }

        try
        {
            if (movieScheduleEnabled is { } me)
            {
                after = after with
                {
                    MovieScheduleEnabled = me,
                    MovieScheduleHoursLimited = movieScheduleHoursLimited ?? false,
                    MovieScheduleDays = ScheduleWindow.ValidateDaysCsv(movieScheduleDays),
                    MovieScheduleStart = ScheduleWindow.NormalizeHhmm(movieScheduleStart, "00:00"),
                    MovieScheduleEnd = ScheduleWindow.NormalizeHhmm(movieScheduleEnd, "23:59"),
                };
            }

            if (tvScheduleEnabled is { } te)
            {
                after = after with
                {
                    TvScheduleEnabled = te,
                    TvScheduleHoursLimited = tvScheduleHoursLimited ?? false,
                    TvScheduleDays = ScheduleWindow.ValidateDaysCsv(tvScheduleDays),
                    TvScheduleStart = ScheduleWindow.NormalizeHhmm(tvScheduleStart, "00:00"),
                    TvScheduleEnd = ScheduleWindow.NormalizeHhmm(tvScheduleEnd, "23:59"),
                };
            }
        }
        catch (ScheduleWindowException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await _operatorSettings.UpdateAsync(uow, before, after).ConfigureAwait(false);
        var updated = await _operatorSettings.EnsureAsync(uow).ConfigureAwait(false);
        var suite = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        // The periodic-scan switches are among these settings; the scheduler reads them again on its next tick.
        _scanSettingsChanges.Record();
        return ApiRoutes.Ok(OperatorSettingsOut(updated, string.IsNullOrWhiteSpace(suite.AppTimezone) ? "UTC" : suite.AppTimezone.Trim()));
    }
}
