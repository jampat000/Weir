using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Time;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    /// <summary>
    /// A file Weir could not read from start to finish is looked at again after a while, and only a few times (#646).
    /// </summary>
    /// <remarks>
    /// Reading a file to the end is how Weir tells a download that is still arriving from one that is damaged, and it cannot
    /// tell them apart from one look. So the look is repeated, further apart each time, while the file is still changing or
    /// might be, and not for ever: otherwise every scan would reread the whole file, gigabytes of disk reads every few
    /// minutes for a file nobody is ever told about. Once the file has sat unchanged through <see cref="UnreadableWaitMinutes"/>, it is
    /// damaged, so this hands it to the library's failure policy with the evidence a reject needs (#471) — the same place a
    /// file whose contents cannot be read at all ends up. A file that changes starts the count again.
    /// </remarks>
    private async Task SettleUnreadableSourceAsync(long jobId, WireObject data, WireObject? origin, WireObject result, CancellationToken cancellationToken)
    {
        if (result.Get("not_ready_kind") is not WireString { Value: RemuxPassRunner.UnreadableWait })
        {
            return;
        }

        var fingerprint = SourceFingerprint(result.Get("inspected_source_path") as WireString);
        var last = data.Get("unreadable_looks") is WireInteger counted ? (long)counted.Value : 0;
        // A look Weir could not measure the file for decides nothing: it neither counts against the file nor clears its
        // record. Only a look that found the same size and time as the one before is another look at the same file.
        var looks = fingerprint is null
            ? Math.Max(1, last)
            : data.Get("unreadable_fingerprint") is WireString previous && previous.Value == fingerprint
                ? last + 1
                : 1;
        if (_jobs is null)
        {
            // Nothing can queue the next look, so the file keeps waiting as it did before, rather than be called damaged
            // on the strength of one read.
            return;
        }

        var waited = UnreadableWaitMinutes.Take((int)Math.Min(looks - 1, UnreadableWaitMinutes.Count)).Sum();
        if (looks > UnreadableWaitMinutes.Count)
        {
            var reason = result.Get("reason") is WireString said ? said.Value : "Weir could not read this file from start to finish.";
            var sentence =
                $"Weir looked at this file {looks.ToString(CultureInfo.InvariantCulture)} times over about " +
                $"{waited.ToString(CultureInfo.InvariantCulture)} minutes and could not read it from start to finish, and it has not " +
                $"changed since the first look. It is damaged or incomplete rather than still arriving. {reason}";
            result.Remove("retryable_wait");
            result.Remove("not_ready_kind");
            result.Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
                .Set("preflight_status", "failed")
                .Set("preflight_reason", sentence)
                .Set("reason", sentence)
                // Evidence the release is bad, so a library set to reject can act on it (#471).
                .Set("content_unusable", true);
            _logger.LogWarning("A file did not read to the end after {Looks} looks, so Weir stopped waiting for it: job {JobId}.", looks, jobId);
            return;
        }

        var lookAgainAt = _time.GetUtcNow().AddMinutes(UnreadableWaitMinutes[(int)looks - 1]);
        var payload = data.Copy().Set("unreadable_looks", looks);
        if (fingerprint is not null)
        {
            payload.Set("unreadable_fingerprint", fingerprint);
        }

        if (origin is { IsTruthy: true })
        {
            payload.Set("origin", origin);
        }

        try
        {
            await _jobs.EnqueueOrGetAsync(
                $"{RemuxPassOutcomes.JobKind}:unreadable-wait:{jobId}:{looks.ToString(CultureInfo.InvariantCulture)}",
                RemuxPassOutcomes.JobKind,
                WireJsonWriter.Dumps(payload, WireJsonFormat.Compact),
                cancellationToken: cancellationToken,
                notBefore: lookAgainAt).ConfigureAwait(false);
            result.Set("retry_scheduled", true).Set("failure_next_retry_at", Timestamp.FromDateTimeOffset(lookAgainAt).IsoFormat());
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The file stays on hold with its reason, which is true; it is only the next look that is missing.
            _logger.LogWarning(exception, "Weir could not queue another look at a file that would not read to the end.");
        }
    }

    /// <summary>The size and modification time of the file this look read, as one string, or null when it cannot be read.</summary>
    private static string? SourceFingerprint(WireString? inspectedSourcePath)
    {
        if (inspectedSourcePath is not { Value.Length: > 0 } path)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path.Value);
            return info.Exists
                ? $"{info.Length.ToString(CultureInfo.InvariantCulture)}:{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}"
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file that is only too young is looked at again once it is old enough (#632).
    /// </summary>
    /// <remarks>
    /// The folder scan has always done this for itself: a young file is on hold until the next scan. A hand-off has no
    /// next scan - a library fed only by hand-offs may have scanning switched off - so the second look is a job, held back
    /// with <c>not_before</c> exactly as a retry's backoff is. It carries the hand-off's origin, so the outcome is still
    /// reported to the media manager that sent it; marking this result <c>retry_scheduled</c> is what stops the reporter
    /// telling that manager anything yet, because nothing is final. Nothing here is specific to any one manager.
    /// </remarks>
    private async Task DeferUntilOldEnoughAsync(long jobId, WireObject data, WireObject? origin, WireObject result, CancellationToken cancellationToken)
    {
        if (_jobs is null || result.Get("not_ready_kind") is not WireString { Value: RemuxPassRunner.MinimumAgeWait })
        {
            return;
        }

        var waits = data.Get("minimum_age_waits") is WireInteger counted ? (long)counted.Value : 0;
        if (waits >= MaxMinimumAgeWaits)
        {
            var stopped = "This file has kept changing, so Weir has stopped looking at it. When the copy has finished, use Check again from Files.";
            result.Set("reason", stopped).Set("preflight_reason", stopped).Set("failure_operator_message", stopped);
            _logger.LogWarning("A file was still changing after {Waits} looks, so Weir stopped looking: job {JobId}.", waits, jobId);
            return;
        }

        var seconds = result.Get("not_ready_seconds") is WireInteger remaining ? Math.Max(1, (long)remaining.Value) : 60;
        // A little past the moment it is old enough, so the second look does not land a fraction of a second early.
        var lookAgainAt = _time.GetUtcNow().AddSeconds(seconds + 2);
        var payload = data.Copy().Set("minimum_age_waits", waits + 1);
        if (origin is { IsTruthy: true })
        {
            payload.Set("origin", origin);
        }

        try
        {
            await _jobs.EnqueueOrGetAsync(
                $"{RemuxPassOutcomes.JobKind}:minimum-age-wait:{jobId}:{waits + 1}",
                RemuxPassOutcomes.JobKind,
                WireJsonWriter.Dumps(payload, WireJsonFormat.Compact),
                cancellationToken: cancellationToken,
                notBefore: lookAgainAt).ConfigureAwait(false);
            result.Set("retry_scheduled", true).Set("failure_next_retry_at", Timestamp.FromDateTimeOffset(lookAgainAt).IsoFormat());
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            // The file stays on hold with its reason, which is true; it is only the second look that is missing.
            _logger.LogWarning(exception, "Weir could not queue a second look at a file that is waiting out its minimum age.");
        }
    }
}
