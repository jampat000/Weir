using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Core.Validation;
using Weir.Infrastructure.Logging;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>Suite settings, the configuration bundle (export/import), configuration-backup listing and download, and the security overview.</summary>
public static class SuiteConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapSuiteConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Suite settings. One URL per handler, with no alias spellings: the shipped web app is built from
        // this repo, and a second address for the same handler is a second thing to secure, document and test.
        endpoints.MapV1("GET", "/suite/settings", GetSettingsAsync);
        endpoints.MapV1("PUT", "/suite/settings", PutSettingsAsync);
        endpoints.MapV1("GET", "/suite/configuration-bundle", GetBundleAsync);
        endpoints.MapV1("PUT", "/suite/configuration-bundle", PutBundleAsync);
        endpoints.MapV1("GET", "/suite/configuration-backups", GetBackupsAsync);
        endpoints.MapV1("GET", "/suite/configuration-backups/{backup_id}/download", DownloadBackupAsync);
        endpoints.MapV1("GET", "/suite/security-overview", GetSecurityOverviewAsync);
        return endpoints;
    }

    private static async Task<ApiResult> GetSettingsAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(SuiteSettingsRules.BuildOut(await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false)));
    }

    private static async Task<ApiResult> PutSettingsAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var name = model.Str("product_display_name", minLength: 1, maxLength: 120);
        var notice = model.OptionalStr("signed_in_home_notice", maxLength: 4000);
        var wizard = model.OptionalStr("setup_wizard_state", minLength: 1, maxLength: 32);
        var timezone = model.Str("app_timezone", minLength: 1, maxLength: 120);
        var logRetention = model.Number("log_retention_days", 0, required: true, ge: 1, le: 3650);
        var activityRetention = model.OptionalInt("activity_retention_days", ge: 0, le: 3650);
        var backupEnabled = model.OptionalBool("configuration_backup_enabled");
        var backupHours = model.OptionalInt("configuration_backup_interval_hours", ge: 1, le: 720);
        var backupTime = model.OptionalStr("configuration_backup_preferred_time", minLength: 5, maxLength: 5);
        model.Finish(ExtraFields.Ignore);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        PyDict output;
        SuiteSettingsRecord updated;
        try
        {
            var normalized = SuiteSettingsRules.Normalize(
                new SuiteSettingsUpdate(name, notice, timezone, logRetention, wizard, activityRetention, backupEnabled, backupHours, backupTime),
                request.Service<ITimeZoneResolver>());
            var before = await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
            await SuiteSettingsStore.UpdateAsync(uow, before, SuiteSettingsRules.Apply(before, normalized)).ConfigureAwait(false);
            updated = await SuiteSettingsStore.EnsureAsync(uow).ConfigureAwait(false);
            output = SuiteSettingsRules.BuildOut(updated);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        PruneLog(request, (int)Math.Max(1, Math.Min(3650, updated.LogRetentionDays)));
        return ApiRoutes.Ok(output);
    }

    /// <summary>
    /// The settings are already saved, so a log that cannot be pruned (for example one holding invalid UTF-8)
    /// is logged and never turns the response into a 500 (#536).
    /// </summary>
    private static void PruneLog(ApiRequest request, int keepDays)
    {
        var logger = request.LoggerFactory.CreateLogger("weir.core.logging");
        try
        {
            if (!request.Service<WeirLogFile>().Prune(keepDays))
            {
                logger.LogWarning("Suite log prune skipped because the active log could not be rewritten.");
            }
        }
#pragma warning disable CA1031 // Pruning the log is housekeeping; a failure is logged and the request still succeeds.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(exception, "Suite log prune skipped because the active log could not be rewritten.");
        }
    }

    private static async Task<ApiResult> GetBundleAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        try
        {
            return ApiRoutes.Ok(await ConfigurationBundleStore.BuildAsync(uow).ConfigureAwait(false));
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status500InternalServerError, exception.Message);
        }
    }

    private static async Task<ApiResult> PutBundleAsync(ApiRequest request)
    {
        var body = await request.ReadBodyAsync().ConfigureAwait(false);
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(body, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var bundle = model.Dict("bundle");
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        request.RequireConfirmationToken(csrfToken);
        var uow = await request.DbAsync().ConfigureAwait(false);
        try
        {
            await ConfigurationBundleStore.ApplyAsync(uow, bundle, request.Service<ITimeZoneResolver>(), request.Options.WeirHome).ConfigureAwait(false);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, exception.Message);
        }

        await request.CommitAsync().ConfigureAwait(false);
        request.Service<ScanSettingsChanges>().Record();
        return ApiRoutes.Ok(await ConfigurationBundleStore.BuildAsync(uow).ConfigureAwait(false));
    }

    private static async Task<ApiResult> GetBackupsAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var backups = request.Service<ConfigurationBackups>();
        var rows = await ConfigurationBackups.ListAsync(uow).ConfigureAwait(false);
        return ApiRoutes.Ok(new PyDict()
            .Set("directory", backups.Directory)
            .Set("items", new PyList(rows.Select(row => (PyJson)ConfigurationBackups.ItemOut(row)))));
    }

    private static async Task<ApiResult> DownloadBackupAsync(ApiRequest request)
    {
        await request.RequireUserAsync(UserRoles.AdminOnly).ConfigureAwait(false);
        var issues = new ValidationIssues();
        var backupId = request.PathInt("backup_id", issues);
        issues.ThrowIfAny();
        var uow = await request.DbAsync().ConfigureAwait(false);
        string path;
        ConfigurationBackupRecord row;
        try
        {
            (path, row) = await request.Service<ConfigurationBackups>().GetFileAsync(uow, backupId).ConfigureAwait(false);
        }
        catch (PyValueErrorException exception)
        {
            throw new ApiException(StatusCodes.Status404NotFound, exception.Message);
        }

        return new CustomApiResult(context => WriteFileAsync(context, path, row.FileName, "application/json"));
    }

    /// <summary>
    /// A file download with disposition, length, last-modified and an MD5 ETag of mtime and size, the headers
    /// existing clients and the contract suite expect.
    /// </summary>
    internal static async Task WriteFileAsync(HttpContext context, string path, string fileName, string mediaType)
    {
        var info = new FileInfo(path);
        var bytes = await File.ReadAllBytesAsync(path, context.RequestAborted).ConfigureAwait(false);
        var mtimeTicks = info.LastWriteTimeUtc.Ticks - DateTime.UnixEpoch.Ticks;
        var mtime = (mtimeTicks / TimeSpan.TicksPerSecond) + ((mtimeTicks % TimeSpan.TicksPerSecond) * 100 * 1e-9);
#pragma warning disable CA5351 // The ETag is an MD5 of mtime and size; it is not a security control.
        var etag = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(PyJsonWriter.FloatRepr(mtime) + "-" + info.Length.ToString(CultureInfo.InvariantCulture))));
#pragma warning restore CA5351
        var quoted = Uri.EscapeDataString(fileName);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = mediaType;
        context.Response.Headers.ContentDisposition = quoted != fileName ? $"attachment; filename*=utf-8''{quoted}" : $"attachment; filename=\"{fileName}\"";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.LastModified = info.LastWriteTimeUtc.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
        context.Response.Headers.ETag = $"\"{etag}\"";
        context.Response.Headers.AcceptRanges = "bytes";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<ApiResult> GetSecurityOverviewAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(SecurityOverview.Build(request.Options));
    }
}
