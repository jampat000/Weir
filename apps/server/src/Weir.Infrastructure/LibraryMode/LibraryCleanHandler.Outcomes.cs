using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Library;
using Weir.Core.LibraryMode;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Text;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// <see cref="LibraryCleanHandler"/>'s post-swap concern: what happens once <see cref="SafeSwap.RunAsync"/> has
/// returned — committed bookkeeping, the in-use retry backoff, and the shared Activity-recording and formatting
/// helpers both outcomes use.
/// </summary>
public sealed partial class LibraryCleanHandler
{
    private async Task OnCommittedAsync(ProcessingLibraryRecord library, string path, LibraryFilePlanResult plan, SwapResult result, CancellationToken cancellationToken)
    {
        // #509 step 1: record what this clean removed for good, keyed the same way library mode identifies the
        // file everywhere else (library id + this path). A future rule change can then ask #509's diff whether
        // any of it would now be kept. Best-effort: a store failure here must never undo an already-committed swap.
        if (plan.Plan is { RemovedTrackRecords.Count: > 0 } committedPlan)
        {
            try
            {
                var key = new RemovedTrackFileKey(library.Id, path);
                await _removedTrackStore.RecordAsync(key, committedPlan.RemovedTrackRecords, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Best-effort, like the notify step below: recording removed tracks must never fail a committed clean.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogWarning(exception, "Library clean committed but recording its removed tracks (#509) failed.");
            }
        }

        // #507 (telling the manager) records its own Activity entries for a notify failure or warning; this handler's own
        // "cleaned" entry below never depends on how that call went.
        try
        {
            var reason = RemovedTracks(plan.RemovedAudioCount, plan.RemovedSubtitleCount);
            await _notifier.NotifyAsync(new LibraryFileChange(library.MediaType, path, Reason: reason), cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The notify step is best-effort and must never fail a committed clean.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Library clean committed but the manager notify step (#507) failed to run.");
        }

        // The scan rewrites its index from scratch, so "Weir cleaned this" is kept where a rescan cannot reach it.
        try
        {
            await using var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
            await LibraryFileMarksStore.MarkCleanedAsync(uow, library.Id, path, _time.GetUtcNow()).ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best-effort, like the steps above: a committed clean is never undone by bookkeeping.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Library clean committed but recording that it was cleaned failed.");
        }

        var warnings = result.Warnings;
        // #735: says where the original was kept, so Activity and History both carry it (History reads this same detail).
        var keptNote = result.KeptOriginalPath is { } keptPath ? $" The original was kept at {keptPath}." : string.Empty;
        var detail = $"Cleaned {Path.GetFileName(path)}: {RemovedTracks(plan.RemovedAudioCount, plan.RemovedSubtitleCount)}." +
                     keptNote + (warnings.Count > 0 ? " " + string.Join(" ", warnings) : string.Empty);
        await RecordAsync(library.Id, path, null, LibraryActivityEventTypes.FileCleaned, detail, "success", result.KeptOriginalPath).ConfigureAwait(false);
    }

    /// <summary>
    /// A locked file is not a failure (#506): back off 5, 15 then 60 minutes by putting the row back to <c>pending</c> with a
    /// future <c>not_before</c> and clearing the lease ourselves — <see cref="ProcessingJobStore.CompleteClaimedAsync"/>'s own
    /// lease check then finds the lease already gone and leaves this update alone. After the third attempt, report it as
    /// given up and let the job complete normally.
    /// </summary>
    private async Task OnInUseAsync(JobWorkContext context, PyDict payload, long libraryId, string path, string trigger, int inUseAttempts)
    {
        var attempt = inUseAttempts + 1;
        var delay = SafeSwapRules.InUseRetryDelay(attempt);
        if (delay is null)
        {
            await RecordAsync(libraryId, path, trigger, LibraryActivityEventTypes.FileFailed, SafeSwapRules.InUseGaveUpMessage).ConfigureAwait(false);
            return;
        }

        payload.Set("in_use_attempts", attempt);
        var notBefore = _time.GetUtcNow() + delay.Value;
        await using var uow = await UnitOfWork.OpenAsync(_database, CancellationToken.None).ConfigureAwait(false);
        await uow.ExecuteAsync(
            "UPDATE jobs SET payload_json = @payload, status = @pending, lease_owner = NULL, lease_expires_at = NULL, not_before = @notBefore, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@payload", PyJsonWriter.Dumps(payload, PyJsonFormat.Compact)),
            ("@pending", ProcessingJobStatus.Pending),
            ("@notBefore", notBefore.UtcDateTime),
            ("@id", context.Id)).ConfigureAwait(false);
        await uow.CommitAsync().ConfigureAwait(false);
        _logger.LogInformation("Library clean postponed (in use, attempt {Attempt}) job_id={JobId} path={Path}", attempt, context.Id, path);
    }

    private async Task RecordAsync(long libraryId, string path, string? trigger, string eventType, string detail, string? result = null, string? keptOriginalPath = null)
    {
        try
        {
            await using var uow = await UnitOfWork.OpenAsync(_database, CancellationToken.None).ConfigureAwait(false);
            var extra = new PyDict().Set("library_id", libraryId).Set("relative_path", path);
            if (trigger is not null)
            {
                extra.Set("trigger", trigger);
            }

            if (result is not null)
            {
                extra.Set("result", result);
            }

            if (keptOriginalPath is not null)
            {
                extra.Set("kept_original_path", keptOriginalPath);
            }

            await SqliteActivityWriter.RecordAsync(
                    uow,
                    new ActivityEventDraft(eventType, "library", detail, PyJsonWriter.Dumps(extra, PyJsonFormat.Compact)))
                .ConfigureAwait(false);
            await uow.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Library mode could not record its activity entry; the outcome remains only in the job row.");
        }
    }

    /// <summary>"removed 1 audio track and 2 subtitle tracks", naming only the kinds that lost a track.</summary>
    internal static string RemovedTracks(int audio, int subtitles)
    {
        var parts = new List<string>(2);
        if (audio > 0)
        {
            parts.Add(Plural.Of(audio, "audio track"));
        }

        if (subtitles > 0)
        {
            parts.Add(Plural.Of(subtitles, "subtitle track"));
        }

        return parts.Count == 0 ? "removed no audio or subtitle tracks" : "removed " + string.Join(" and ", parts);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
