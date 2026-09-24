using Microsoft.Data.Sqlite;
using Weir.Core.Configuration;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>An intake request refused with an HTTP status and a detail.</summary>
public sealed class IntakeRefusedException : Exception
{
    public IntakeRefusedException()
    {
    }

    public IntakeRefusedException(string message)
        : base(message)
    {
    }

    public IntakeRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IntakeRefusedException(int statusCode, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; } = 400;
}

/// <summary>
/// Who an intake caller proved itself as. <see cref="ConnectionId"/> is the specific connection whose own secret
/// matched, when one did; it is <see langword="null"/> when the caller proved itself only through the instance-wide
/// secret (or, for a single unambiguous connection with no secret, is left unauthenticated but still attributable).
/// A hand-off route that reveals file paths must accept only the secret of the connection its own row names as
/// owner, never any other connection's secret of the same kind.
/// </summary>
public readonly record struct MediaManagerIntakeIdentity(bool Authenticated, long? ConnectionId);

/// <summary>
/// The intake webhook's work: who may post, which library a hand-off belongs to, which files
/// it means, and the remux jobs and ledger row it leaves behind.
/// </summary>
public sealed class MediaManagerIntake
{
    private readonly WeirOptions _options;
    private readonly MediaManagerConnectionService _connections;
    private readonly MediaManagerConnectionStore _connectionStore;
    private readonly HandoffLedgerStore _ledger;
    private readonly HandoffTargetStore _targets;
    private readonly ProcessingJobStore _jobs;
    private readonly TimeProvider _time;

    public MediaManagerIntake(
        WeirOptions options,
        MediaManagerConnectionService connections,
        MediaManagerConnectionStore connectionStore,
        HandoffLedgerStore ledger,
        HandoffTargetStore targets,
        ProcessingJobStore jobs,
        TimeProvider time)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>For tests: where fresh dedupe ids come from when a hand-off names none.</summary>
    public Func<Guid> NewGuid { get; init; } = Guid.NewGuid;

    public HandoffLedgerStore Ledger => _ledger;

    public ProcessingJobStore Jobs => _jobs;

    /// <summary>
    /// Authorise a webhook: this source's own connection secret when it has one, else the instance-wide secret, else no
    /// check. The presented secret is matched against every enabled connection of the kind (#544 item 6), so a second
    /// connection of the same kind (a 4K Radarr next to a 1080p one, each with its own secret) can authenticate too, and
    /// the event is attributed to whichever one matches. The instance-wide secret is the fallback only when none of them
    /// has a secret configured; once any connection of this kind uses its own secret, its callers must present it.
    /// </summary>
    /// <remarks>
    /// An unchecked "imported" is still recorded, but it never removes a file (#652). The connection-less native
    /// source has no address of its own to prove who is calling, so it refuses a write once nobody has ever
    /// configured a secret for it, rather than accepting one unchecked as a real connection does. A kind with no
    /// connection at all is refused the same way: an existing connection that never rotated its secret keeps
    /// accepting unsigned webhooks (upgrades must not break), but there is nothing to attribute a webhook to
    /// when the kind was never set up in the first place.
    /// </remarks>
    public async Task<MediaManagerIntakeIdentity> AuthoriseAsync(UnitOfWork uow, string sourceKey, string? presented)
    {
        var connections = await _connectionStore.ListEnabledForKindAsync(uow, sourceKey).ConfigureAwait(false);
        var withSecret = connections.Where(connection => !string.IsNullOrEmpty(connection.WebhookSecretCiphertext)).ToList();
        if (withSecret.Count > 0)
        {
            var matched = withSecret.FirstOrDefault(connection => _connections.WebhookSecretMatches(connection, presented));
            if (matched is null)
            {
                throw new IntakeRefusedException(401, IntakeRules.MissingSecretDetail);
            }

            return new MediaManagerIntakeIdentity(Authenticated: true, matched.Id);
        }

        // Attributable even when unchecked: exactly one connection of the kind is the only one this event could be
        // for, whether or not it has bothered to rotate its own secret.
        var soleConnectionId = connections.Count == 1 ? connections[0].Id : (long?)null;
        var configured = _options.MediaManagerWebhookSecret;
        if (string.IsNullOrEmpty(configured))
        {
            if (sourceKey == MediaManagerKinds.Native)
            {
                throw new IntakeRefusedException(401, IntakeRules.NativeNeedsSecretDetail);
            }

            if (connections.Count == 0)
            {
                throw new IntakeRefusedException(401, IntakeRules.NoConnectionDetail(sourceKey));
            }

            return new MediaManagerIntakeIdentity(Authenticated: false, soleConnectionId);
        }

        var provided = WireStrings.Strip(presented ?? string.Empty);
        if (provided.Length == 0 || !MediaManagerConnectionService.CompareDigest(provided, configured))
        {
            throw new IntakeRefusedException(401, IntakeRules.MissingSecretDetail);
        }

        return new MediaManagerIntakeIdentity(Authenticated: true, soleConnectionId);
    }

    /// <summary>
    /// Require a secret: the hand-off routes reveal file paths, so unlike the webhook they never run unauthenticated.
    /// Returns who the secret proved, exactly as <see cref="AuthoriseAsync"/> does, so a caller with a specific
    /// hand-off in hand can refuse a secret that proves the wrong connection.
    /// </summary>
    public async Task<MediaManagerIntakeIdentity> RequireSecretAsync(UnitOfWork uow, string? presented, string? sourceKey)
    {
        var provided = WireStrings.Strip(presented ?? string.Empty);
        var rows = await _connectionStore.ListEnabledWithWebhookSecretAsync(uow).ConfigureAwait(false);
        if (sourceKey is not null)
        {
            rows = [.. rows.Where(row => row.Kind == sourceKey)];
        }

        var configured = _options.MediaManagerWebhookSecret;
        if (rows.Count == 0 && string.IsNullOrEmpty(configured))
        {
            throw new IntakeRefusedException(403, IntakeRules.NeedsSecretDetail);
        }

        if (provided.Length > 0)
        {
            if (rows.FirstOrDefault(row => _connections.WebhookSecretMatches(row, provided)) is { } matched)
            {
                return new MediaManagerIntakeIdentity(Authenticated: true, matched.Id);
            }

            if (!string.IsNullOrEmpty(configured) && MediaManagerConnectionService.CompareDigest(provided, configured))
            {
                return new MediaManagerIntakeIdentity(Authenticated: true, ConnectionId: null);
            }
        }

        throw new IntakeRefusedException(401, IntakeRules.MissingSecretDetail);
    }

    /// <summary>Every library's id, media type and watched folder, in display order.</summary>
    public static Task<List<IntakeLibrary>> ListLibrariesAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return uow.QueryAsync(
            "SELECT id, media_type, watched_folder FROM libraries ORDER BY display_order, id",
            reader => new IntakeLibrary(SqliteValues.GetInt64(reader, 0), SqliteValues.GetString(reader, 1), SqliteValues.GetString(reader, 2)));
    }

    /// <summary>The library a hand-off belongs to, chosen by folder; nothing containing it explains against the scope's seeded library.</summary>
    public static async Task<(IntakeLibrary? Library, HandoffPathResult Resolved)> LibraryForHandoffAsync(UnitOfWork uow, MediaManagerImportEvent importEvent)
    {
        ArgumentNullException.ThrowIfNull(importEvent);
        var libraries = await ListLibrariesAsync(uow).ConfigureAwait(false);
        if (IntakeRules.ChooseLibrary(libraries, importEvent) is { } chosen)
        {
            return (chosen.Library, chosen.Resolved);
        }

        var scope = ProcessingMediaScopes.Normalize(importEvent.MediaScope);
        var fallback = libraries.FirstOrDefault(library => library.MediaType == scope);
        return (fallback, HandoffPaths.RelativeMediaPathForHandoff(fallback?.WatchedFolder ?? string.Empty, importEvent.FilePath));
    }

    /// <summary>
    /// The media files a hand-off means: a file names itself; a folder means the videos inside it with samples left out.
    /// </summary>
    public static List<string> HandoffMediaFiles(IntakeLibrary? library, string relativePath)
    {
        if (library is null || relativePath.Length == 0)
        {
            return [relativePath];
        }

        var windows = OperatingSystem.IsWindows();
        var folder = Path.Join(library.WatchedFolder, relativePath);
        List<IReadOnlyList<string>> videos;
        try
        {
            if (!Directory.Exists(folder))
            {
                return [relativePath];
            }

            videos = [];
            foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
            {
                if (IntakeRules.IsMediaCandidateName(Path.GetFileName(file)))
                {
                    videos.Add(Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [relativePath];
        }

        var chosen = IntakeRules.ChooseFolderVideos(videos, windows);
        if (chosen.Count == 0)
        {
            throw new IntakeRefusedException(400, IntakeRules.NoVideoInFolderDetail(relativePath));
        }

        var prefix = relativePath.Replace('\\', '/').TrimEnd('/');
        return [.. chosen.Select(parts => string.Join('/', new[] { prefix }.Concat(parts).Where(part => part.Length > 0 && part != ".")))];
    }

    /// <summary>
    /// Take in a hand-off: its remux jobs, its ledger row, and (#531) the fingerprint of each file.
    /// <paramref name="ownerConnectionId"/> is who <see cref="AuthoriseAsync"/> attributed the event to, recorded on
    /// the ledger row so a later hand-off route can require that connection's own secret, not any same-kind one.
    /// </summary>
    public async Task<string> EnqueueRefineAsync(UnitOfWork uow, MediaManagerImportEvent importEvent, long? ownerConnectionId = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(importEvent);
        if (!string.IsNullOrEmpty(importEvent.CallbackPath) && !IntakeRules.IsValidCallbackPath(importEvent.CallbackPath))
        {
            throw new IntakeRefusedException(422, IntakeRules.InvalidCallbackPathDetail(importEvent.CallbackPath));
        }

        var (library, resolved) = await LibraryForHandoffAsync(uow, importEvent).ConfigureAwait(false);
        if (!resolved.Ok)
        {
            throw new IntakeRefusedException(400, resolved.Problem!);
        }

        var relativePath = resolved.RelativeMediaPath!;
        var targets = HandoffMediaFiles(library, relativePath);
        var baseKey = IntakeRules.BaseDedupeKey(importEvent, NewGuid);
        var covered = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            var dedupeKey = IntakeRules.DedupeKeyFor(baseKey, targets, target, relativePath);
            var payload = IntakeRules.Payload(importEvent, library, relativePath, target);
            var connection = uow.Connection;
            var transaction = uow.WriteTransaction();

            // Folder detection may already have queued (or started) this very file under its own random key. One file
            // gets one pass: the hand-off takes over that pass rather than adding a second one. A resend of this same
            // hand-off still lands on its own row through EnqueueOrGet below.
            if (library is not null &&
                ProcessingJobStore.GetByDedupeKey(connection, transaction, dedupeKey) is null &&
                ActiveRemuxPasses.ForRelativePath(connection, transaction, target, library.MediaType, library.Id) is { } active)
            {
                if (AdoptActivePass(connection, transaction, active, importEvent, dedupeKey, payload))
                {
                    covered.Add(target);
                }

                continue;
            }

            _jobs.EnqueueOrGet(connection, transaction, dedupeKey, IntakeRules.RemuxPassJobKind, IntakeRules.PayloadJson(payload), JobQueueRules.DefaultMaxAttempts, 0, 0);
            covered.Add(target);
        }

        if (!string.IsNullOrEmpty(importEvent.HandoffId))
        {
            var rowId = await _ledger.RecordReceivedAsync(
                uow, importEvent.SourceKey, importEvent.HandoffId, library?.Id, relativePath, ownerConnectionId, importEvent.DownloadId).ConfigureAwait(false);
            await _targets.AddAsync(uow, rowId, covered).ConfigureAwait(false);
        }

        if (library is not null)
        {
            foreach (var target in targets)
            {
                await RecordFingerprintAsync(uow, library, target).ConfigureAwait(false);
            }
        }

        return IntakeRules.RemuxPassJobKind;
    }

    /// <summary>
    /// A hand-off for a file that already has a pending or leased pass (queued by folder detection, an automatic retry
    /// or a user) makes that pass its own instead of queuing a second one: the job is re-keyed to the hand-off's dedupe
    /// key, so <c>GET /api/v1/intake/handoffs/{kind}/{id}</c> finds it (queue position, working), and it takes the
    /// hand-off's origin, so the outcome is called back to the manager. A pass that is already running picks the origin
    /// up when it finishes (<see cref="Processing.RemuxPass.RemuxPassHandler"/>). A pass that already belongs to
    /// another hand-off is left with it: this hand-off is still recorded and answers from the file's own state, and the
    /// file is not one this hand-off waits for before it reports. True when the hand-off took the pass over.
    /// </summary>
    private static bool AdoptActivePass(
        SqliteConnection connection, SqliteTransaction transaction, ProcessingJob active, MediaManagerImportEvent importEvent, string dedupeKey, WireObject handoffPayload)
    {
        WireObject existing;
        try
        {
            existing = WireJsonParser.Parse(string.IsNullOrEmpty(active.PayloadJson) ? "{}" : active.PayloadJson) as WireObject ?? new WireObject();
        }
        catch (WireJsonDecodeException)
        {
            existing = new WireObject();
        }

        if (HandoffOrigin.FromPayload(existing) is { HandoffId: { } ownerId } owner &&
            (owner.SourceKey != importEvent.SourceKey || ownerId != importEvent.HandoffId))
        {
            return false;
        }

        if (handoffPayload.Get("origin") is WireObject origin)
        {
            existing.Set("origin", origin);
        }

        existing.Set("trigger", "webhook");
        var newKey = string.IsNullOrEmpty(importEvent.HandoffId) ? active.DedupeKey : dedupeKey;
        ProcessingJobStore.Execute(
            connection,
            transaction,
            "UPDATE jobs SET dedupe_key = @dedupe, payload_json = @payload, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@dedupe", newKey),
            ("@payload", IntakeRules.PayloadJson(existing)),
            ("@id", active.Id));
        return true;
    }

    /// <summary>
    /// Record a handed-over file's size when it arrives (#531). Otherwise the next watched-folder scan would read the zero
    /// a failure left on the row as a change and reset the failure count, so the retry limit and the hold after repeated
    /// failures would never apply. A file that is missing, or a row that already has a size, is left alone; a later scan
    /// still sees a genuinely changed file as changed.
    /// </summary>
    private async Task RecordFingerprintAsync(UnitOfWork uow, IntakeLibrary library, string relativePath)
    {
        long size;
        try
        {
            var info = new FileInfo(Path.Join(library.WatchedFolder, relativePath));
            if (!info.Exists)
            {
                return;
            }

            size = info.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return;
        }

        // No status reason: the queued job speaks for the file until a pass or scan records one, and a reason here
        // would change what the hand-off status tells the manager while the file waits.
        await uow.ExecuteAsync(
            "INSERT INTO files (library_id, relative_path, status, status_reason, size_bytes, last_seen_at) " +
            "VALUES ($library, $path, 'unprocessed', '', $size, $now) " +
            "ON CONFLICT (library_id, relative_path) DO UPDATE SET size_bytes = excluded.size_bytes, updated_at = CURRENT_TIMESTAMP " +
            "WHERE files.size_bytes = 0 AND excluded.size_bytes <> 0",
            ("$library", library.Id),
            ("$path", relativePath),
            ("$size", size),
            ("$now", TimestampColumns.Orm(_time.GetUtcNow()))).ConfigureAwait(false);
    }
}
