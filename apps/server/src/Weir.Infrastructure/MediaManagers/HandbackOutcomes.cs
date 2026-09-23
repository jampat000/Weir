using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>What Weir did with a Sonarr or Radarr "imported" message (#652).</summary>
/// <param name="Matched">The message was about a file Weir handed back.</param>
/// <param name="Released">Weir removed its copy.</param>
/// <param name="Message">What happened, in plain words.</param>
/// <param name="RelativePath">The file's path in the watched folder, when matched.</param>
public sealed record ManagerImportResult(bool Matched, bool Released, string Message, string? RelativePath = null);

/// <summary>What Weir did with a manager's outcome for one of its hand-offs (#652).</summary>
public sealed record HandoffOutcomeResult(bool Released, string Message);

/// <summary>
/// A manager's word on a file Weir handed back (#652): Sonarr's and Radarr's import webhook, and the hand-off outcome
/// Deluno sends. Both record what the manager said, so the file's History shows it, and both release Weir's copy only
/// through <see cref="HandbackStore.Release"/>.
/// </summary>
public sealed class HandbackOutcomes
{
    private readonly HandoffLedgerStore _ledger;
    private readonly TimeProvider _time;

    public HandbackOutcomes(HandoffLedgerStore ledger, TimeProvider time)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Sonarr or Radarr imported a file. When it is one Weir handed back, record it and release Weir's copy by the shared
    /// rule. <paramref name="authenticated"/> says the message carried a webhook secret: without one it is recorded but never
    /// removes anything, since anybody could have sent it. A file Weir never handed back changes nothing.
    /// </summary>
    public async Task<ManagerImportResult> RecordManagerImportAsync(UnitOfWork uow, MediaManagerImportEvent importEvent, string manager, bool authenticated)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(importEvent);
        var row = await MatchAsync(uow, importEvent).ConfigureAwait(false);
        if (row is null)
        {
            return new ManagerImportResult(false, false, "Weir did not hand this file back, so there is nothing for it to record.");
        }

        // The same message again (a manager retrying its webhook) changes nothing once Weir has settled the copy.
        if (row.Outcome == HandbackRules.Imported && row.SettledAt is not null)
        {
            return new ManagerImportResult(true, row.ReleasedAt is not null, row.ReleaseNote ?? string.Empty, row.RelativePath);
        }

        var now = _time.GetUtcNow();
        await HandbackStore.RecordOutcomeAsync(uow, row.Id, HandbackRules.Imported, manager, now, importEvent.FilePath, null).ConfigureAwait(false);
        if (row.SettledAt is not null)
        {
            // Weir already stopped looking after this copy (the Cleanup job removed it, say): the import is recorded, and
            // what happened to the copy then still stands.
            await RecordActivityAsync(uow, manager, HandbackRules.Imported, row.LibraryId, row.RelativePath, importEvent.FilePath, null, false, row.ReleaseNote ?? string.Empty, "webhook")
                .ConfigureAwait(false);
            return new ManagerImportResult(true, false, row.ReleaseNote ?? string.Empty, row.RelativePath);
        }

        var release = authenticated
            ? HandbackStore.Release(row, manager, importEvent.FilePath)
            : new HandbackRelease(HandbackReleaseKind.Kept, HandbackRules.UnsignedNote(manager));
        await HandbackStore.RecordReleaseAsync(uow, row.Id, release, now).ConfigureAwait(false);
        await RecordActivityAsync(uow, manager, HandbackRules.Imported, row.LibraryId, row.RelativePath, importEvent.FilePath, null, release.Removed, release.Note, "webhook")
            .ConfigureAwait(false);
        return new ManagerImportResult(true, release.Removed, release.Note, row.RelativePath);
    }

    /// <summary>
    /// A manager's outcome for one of its finished hand-offs: record it on the hand-off and on each of its files' copies.
    /// <c>imported</c> releases each copy by the shared rule; <c>not-imported</c> is final and keeps every copy.
    /// </summary>
    public async Task<HandoffOutcomeResult> RecordHandoffOutcomeAsync(
        UnitOfWork uow, HandoffLedgerRow row, string manager, string outcome, DateTimeOffset occurredAt, string? importedPath, string? reason)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        var now = _time.GetUtcNow();
        int removed = 0, gone = 0, kept = 0;
        string? firstKeptNote = null;
        if (row.LibraryId is { } libraryId)
        {
            foreach (var file in await HandoffLedgerStore.FileRowsAsync(uow, row).ConfigureAwait(false))
            {
                var copy = await HandbackStore.FindAsync(uow, libraryId, file.RelativePath).ConfigureAwait(false);
                if (copy is null)
                {
                    continue;
                }

                await HandbackStore.RecordOutcomeAsync(uow, copy.Id, outcome, manager, occurredAt, importedPath, reason).ConfigureAwait(false);
                HandbackRelease release;
                if (outcome == HandbackRules.NotImported)
                {
                    release = new HandbackRelease(HandbackReleaseKind.Kept, HandbackRules.NotImportedNote(manager, reason));
                }
                else if (copy.SettledAt is not null)
                {
                    // Already settled (Sonarr's own webhook, say, got there first): keep what happened then.
                    release = new HandbackRelease(
                        copy.ReleasedAt is not null ? HandbackReleaseKind.Removed : HandbackReleaseKind.Kept, copy.ReleaseNote ?? HandbackRules.UnrecordedNote);
                }
                else
                {
                    release = HandbackStore.Release(copy, manager, importedPath);
                }

                await HandbackStore.RecordReleaseAsync(uow, copy.Id, release, now).ConfigureAwait(false);
                switch (release.Kind)
                {
                    case HandbackReleaseKind.Removed:
                        removed++;
                        break;
                    case HandbackReleaseKind.AlreadyGone:
                        gone++;
                        break;
                    default:
                        kept++;
                        firstKeptNote ??= release.Note;
                        break;
                }
            }
        }

        var released = outcome == HandbackRules.Imported && removed > 0 && kept == 0;
        var message = HandbackRules.OutcomeMessage(manager, outcome, removed, gone, kept, firstKeptNote);
        await _ledger.RecordManagerOutcomeAsync(uow, row.Id, outcome, occurredAt, message, released).ConfigureAwait(false);
        await RecordActivityAsync(uow, manager, outcome, row.LibraryId, row.RelativePath, importedPath, reason, released, message, "webhook").ConfigureAwait(false);
        return new HandoffOutcomeResult(released, message);
    }

    /// <summary>
    /// The copy a manager's "imported" is about. First by <c>sourcePath</c>: Weir's own path for the copy, or a path that
    /// ends with the copy's whole path below its output folder (the manager's name for the folder is not known, so the
    /// translation <c>HandoffCompletionReporter.TranslateOutputPath</c> makes is reversed from the other end). Then by the
    /// manager's download id, when a hand-off recorded it.
    /// </summary>
    private static async Task<HandbackRow?> MatchAsync(UnitOfWork uow, MediaManagerImportEvent importEvent)
    {
        if (!string.IsNullOrWhiteSpace(importEvent.SourcePath) && HandbackRules.FileName(importEvent.SourcePath) is { Length: > 0 } name)
        {
            var rows = await HandbackStore.WithFileNameAsync(uow, name).ConfigureAwait(false);
            var exact = rows.Where(row => HandbackRules.SameManagerPath(importEvent.SourcePath, row.OutputPath)).ToList();
            var candidates = exact.Count > 0
                ? exact
                : [.. rows.Where(row => HandbackRules.ManagerPathEndsWith(importEvent.SourcePath, HandbackStore.RelativeParts(row)))];
            if (Choose(candidates, importEvent.MediaScope) is { } bySource)
            {
                return bySource;
            }
        }

        if (!string.IsNullOrWhiteSpace(importEvent.DownloadId))
        {
            var found = new List<HandbackRow>();
            foreach (var handoff in await HandoffLedgerStore.WithDownloadIdAsync(uow, importEvent.DownloadId.Trim()).ConfigureAwait(false))
            {
                if (handoff.LibraryId is not { } libraryId)
                {
                    continue;
                }

                foreach (var file in await HandoffLedgerStore.FileRowsAsync(uow, handoff).ConfigureAwait(false))
                {
                    if (await HandbackStore.FindAsync(uow, libraryId, file.RelativePath).ConfigureAwait(false) is { } copy)
                    {
                        found.Add(copy);
                    }
                }
            }

            // A download id covers a whole download; only one copy in it can be the file this message is about.
            return found.Count == 1 ? found[0] : null;
        }

        return null;
    }

    /// <summary>One candidate: of the right kind of library when there is a choice, still looked after, newest.</summary>
    private static HandbackRow? Choose(List<HandbackRow> candidates, string mediaScope)
    {
        if (candidates.Count <= 1)
        {
            return candidates.FirstOrDefault();
        }

        var scope = ProcessingMediaScopes.Normalize(mediaScope);
        var ofScope = candidates.Where(row => ProcessingMediaScopes.Normalize(row.MediaType) == scope).ToList();
        return (ofScope.Count > 0 ? ofScope : candidates)
            .OrderBy(row => row.SettledAt is null ? 0 : 1)
            .ThenByDescending(row => row.WrittenAt ?? DateTimeOffset.MinValue)
            .First();
    }

    private static Task<long> RecordActivityAsync(
        UnitOfWork uow, string manager, string outcome, long? libraryId, string relativePath, string? importedPath, string? reason, bool released, string message, string trigger)
    {
        var detail = new PyDict()
            .Set("relative_media_path", relativePath)
            .Set("library_id", libraryId)
            .Set("provider", manager)
            .Set("outcome", outcome)
            .Set("imported_path", importedPath)
            .Set("reason", reason)
            .Set("released", released)
            .Set("user_message", message)
            .Set("counts", new PyDict().Set("removed", released ? 1 : 0))
            .Set("module", "processing")
            .Set("action", "handback")
            .Set("trigger", trigger)
            .Set("result", "success");
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingHandbackOutcome,
            "processing",
            HandbackRules.OutcomeTitle(manager, outcome, MediaPathNames.Name(relativePath, OperatingSystem.IsWindows())),
            PyStrings.Slice(PyJsonWriter.Dumps(detail, PyJsonFormat.Compact), 10_000)));
    }
}
