using System.Globalization;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Handles <c>processing.unclaimed_handback_cleanup.v1</c> (#652): removes Weir's own hand-back copies that no media manager
/// claimed within the window set in Settings › Cleanup (14 days unless a person changes it). Off until a person switches
/// it on.
/// </summary>
/// <remarks>
/// It removes only copies nobody said anything about: a copy a manager imported is released when it says so, and one a
/// manager said it will not import is kept for a person to deal with. Every removal goes through the same rule as a
/// manager's "imported" (<see cref="HandbackStore.Release"/>): only Weir's own copy, only inside the output folder, and
/// only while it is exactly the file Weir wrote. A copy Weir cannot remove this time (in use) is tried again next run.
/// </remarks>
public sealed partial class UnclaimedHandbackCleanupHandler : IJobHandler
{
    private readonly ProcessingJobStore _store;
    private readonly OperatorSettingsStore _operatorSettings;
    private readonly TimeProvider _time;
    private readonly ILogger<UnclaimedHandbackCleanupHandler> _logger;

    public UnclaimedHandbackCleanupHandler(
        ProcessingJobStore store, OperatorSettingsStore operatorSettings, TimeProvider time, ILogger<UnclaimedHandbackCleanupHandler> logger)
    {
        _store = store;
        _operatorSettings = operatorSettings;
        _time = time;
        _logger = logger;
    }

    public string JobKind => PeriodicJobKinds.UnclaimedHandbackCleanup;

    public async Task HandleAsync(JobWorkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var payload = JobPayload.ParseObject(context.PayloadJson);
        var scope = ProcessingMediaScopes.Normalize(JobPayload.StringProperty(payload, "media_scope"));
        var trigger = JobPayload.StringProperty(payload, "trigger");

        long days;
        List<HandbackRow> rows;
        var read = await Sqlite.UnitOfWork.OpenAsync(_store.Database, cancellationToken).ConfigureAwait(false);
        await using (read.ConfigureAwait(false))
        {
            var settings = await _operatorSettings.GetAsync(read).ConfigureAwait(false);
            days = OperatorSettingsRules.ClampUnclaimedHandbackWindowDays(settings?.UnclaimedHandbackWindowDays ?? HandbackRules.DefaultUnclaimedWindowDays);
            rows = await HandbackStore.UnclaimedAsync(read, scope, _time.GetUtcNow() - TimeSpan.FromDays(days)).ConfigureAwait(false);
        }

        int removed = 0, gone = 0, kept = 0, inUse = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The write lock is taken before the file is touched, so a busy database retries before anything is removed.
            var release = await LockedWrites.RunAsync(
                _store.Database,
                async uow =>
                {
                    uow.BeginImmediate();
                    var decided = HandbackStore.Release(row, "a media manager");
                    decided = decided.Kind switch
                    {
                        HandbackReleaseKind.Removed => decided with { Note = HandbackRules.UnclaimedNote(days) },
                        HandbackReleaseKind.AlreadyGone => decided with { Note = HandbackRules.UnclaimedGoneNote },
                        _ => decided,
                    };
                    await HandbackStore.RecordReleaseAsync(uow, row.Id, decided, _time.GetUtcNow()).ConfigureAwait(false);
                    return decided;
                },
                _logger,
                "unclaimed hand-back cleanup",
                cancellationToken).ConfigureAwait(false);

            switch (release.Kind)
            {
                case HandbackReleaseKind.Removed:
                    removed++;
                    LogRemoved(_logger, row.OutputPath);
                    break;
                case HandbackReleaseKind.AlreadyGone:
                    gone++;
                    break;
                case HandbackReleaseKind.InUse:
                    inUse++;
                    LogInUse(_logger, row.OutputPath, release.Note);
                    break;
                default:
                    kept++;
                    break;
            }
        }

        var label = scope == "tv" ? "TV" : "Movies";
        var detail = new WireObject()
            .Set("job_id", context.Id)
            .Set("media_scope", scope)
            .Set("module", "processing")
            .Set("action", "cleanup")
            .Set("window_days", days)
            .Set("counts", new WireObject()
                .Set("checked", rows.Count)
                .Set("removed", removed)
                .Set("already_gone", gone)
                .Set("skipped", kept)
                .Set("failed", inUse))
            .Set("result", inUse > 0 ? "warning" : "success");
        if (trigger is not null)
        {
            detail.Set("trigger", trigger);
        }

        if (inUse > 0)
        {
            detail.Set("next_action", "Weir could not remove some copies because they are in use. It tries again next run.");
        }

        var title = removed == 0
            ? $"Unclaimed hand-backs checked ({label}): nothing to remove"
            : $"Removed {removed.ToString(CultureInfo.InvariantCulture)} unclaimed hand-back {(removed == 1 ? "copy" : "copies")} ({label})";
        detail.Set("user_message", title + ".");
        await LockedWrites.RunAsync(
            _store.Database,
            uow => SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
                ActivityEventTypes.ProcessingUnclaimedHandbackCleanupCompleted, "processing", title, WireJsonWriter.Dumps(detail, WireJsonFormat.Compact))),
            _logger,
            "unclaimed hand-back cleanup completed",
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The unclaimed hand-back cleanup removed Weir's copy {Path}")]
    private static partial void LogRemoved(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unclaimed hand-back cleanup could not remove {Path}: {Note}")]
    private static partial void LogInUse(ILogger logger, string path, string note);
}
