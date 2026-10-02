using Weir.Core.Paging;
using Weir.Core.Time;

namespace Weir.Core.Processing;

/// <summary>
/// The vocabulary an operator sees on the Files screen.
/// Every status here, including <see cref="PassedThrough"/> and <see cref="Rejected"/>, must be valid in both
/// responses and filters; a schema that omits one turns a file in that state into a 500 on read and a 422 on
/// filter (#530).
/// </summary>
public static class ProcessingFileStatuses
{
    public const string Unprocessed = "unprocessed";
    public const string Processing = "processing";
    public const string Processed = "processed";
    public const string ProcessingFailed = "processing_failed";
    public const string Skipped = "skipped";
    public const string Disabled = "disabled";
    public const string OnHold = "on_hold";
    public const string OutOfSchedule = "out_of_schedule";
    public const string BlockedUpstream = "blocked_upstream";

    /// <summary>Terminal, and deliberately distinct from <see cref="Processed"/>: Weir handed the original
    /// back unmodified rather than keep it (#465).</summary>
    public const string PassedThrough = "passed_through";

    /// <summary>Terminal: the file was rejected, and stays put until a person decides. Under the opt-in <c>reject</c> policy the
    /// manager accepted that the release is bad. With no manager involved, the rules themselves left nothing to keep.</summary>
    public const string Rejected = "rejected";

    /// <summary>Terminal: someone cancelled the file's queued pass before Weir started on it, from the jobs list in System › Logs or through
    /// the media manager that handed it over (#643). The original is untouched, and a scan leaves it alone until the file
    /// changes or someone queues it again.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Every persisted status value, in the order clients see them, with <see cref="Cancelled"/> last (#643).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Unprocessed, Processing, Processed, ProcessingFailed, Skipped, Disabled, OnHold,
        OutOfSchedule, BlockedUpstream, PassedThrough, Rejected, Cancelled,
    ];

    /// <summary>States where Weir has decided not to act, as opposed to not having acted yet.</summary>
    public static readonly IReadOnlySet<string> Withheld = new HashSet<string>(StringComparer.Ordinal)
    {
        Disabled, OnHold, OutOfSchedule, BlockedUpstream, Skipped,
    };

    /// <summary>
    /// States where Weir is done with the file, one way or another. The original usually leaves the watched folder at
    /// that point, so processing one of these again needs its original to still be there.
    /// </summary>
    public static readonly IReadOnlySet<string> Concluded = new HashSet<string>(StringComparer.Ordinal)
    {
        Processed, PassedThrough, Rejected, Cancelled,
    };
}

/// <summary>
/// What a status means to a person, in the order a list sorted by meaning shows them. These are the words and the order
/// of the web's status meanings (<c>lib/ui/status-meaning.ts</c>).
/// </summary>
public enum ProcessingFileMeaning
{
    /// <summary>Finished.</summary>
    Done,

    /// <summary>Waiting its turn.</summary>
    Todo,

    /// <summary>Being worked on right now.</summary>
    Doing,

    /// <summary>Held back, or waiting on a person.</summary>
    Attention,

    /// <summary>Failed.</summary>
    Broken,

    /// <summary>Left alone on purpose.</summary>
    Idle,
}

/// <summary>The one place that says what each of <see cref="ProcessingFileStatuses.All"/> means.</summary>
public static class ProcessingFileMeanings
{
    public static readonly IReadOnlyDictionary<string, ProcessingFileMeaning> OfStatus = new Dictionary<string, ProcessingFileMeaning>(StringComparer.Ordinal)
    {
        [ProcessingFileStatuses.Processed] = ProcessingFileMeaning.Done,
        [ProcessingFileStatuses.Unprocessed] = ProcessingFileMeaning.Todo,
        [ProcessingFileStatuses.OutOfSchedule] = ProcessingFileMeaning.Todo,
        [ProcessingFileStatuses.Processing] = ProcessingFileMeaning.Doing,
        [ProcessingFileStatuses.OnHold] = ProcessingFileMeaning.Attention,
        [ProcessingFileStatuses.BlockedUpstream] = ProcessingFileMeaning.Attention,
        [ProcessingFileStatuses.PassedThrough] = ProcessingFileMeaning.Attention,
        [ProcessingFileStatuses.Rejected] = ProcessingFileMeaning.Attention,
        [ProcessingFileStatuses.ProcessingFailed] = ProcessingFileMeaning.Broken,
        [ProcessingFileStatuses.Skipped] = ProcessingFileMeaning.Idle,
        [ProcessingFileStatuses.Disabled] = ProcessingFileMeaning.Idle,
        [ProcessingFileStatuses.Cancelled] = ProcessingFileMeaning.Idle,
    };

    /// <summary>Where a status stands when files are sorted by meaning; a status with no meaning here follows every one that has.</summary>
    public static int RankOf(string status) =>
        OfStatus.TryGetValue(status, out var meaning) ? (int)meaning : Enum.GetValues<ProcessingFileMeaning>().Length;
}

/// <summary>What a cancelled file says on the Files screen (#643).</summary>
public static class CancelledFileReasons
{
    public const string InWeir =
        "Cancelled from the jobs list in System › Logs before Weir started on it. The original is untouched; queue it again from Activity to process it.";

    public const string ByManager =
        "The media manager cancelled this hand-off before Weir started on it. The original is untouched; queue it again from Activity to process it.";
}

/// <summary>One <c>files</c> row.</summary>
public sealed record ProcessingFileRecord
{
    public long Id { get; init; }
    public long LibraryId { get; init; }
    public required string RelativePath { get; init; }

    public string Status { get; init; } = ProcessingFileStatuses.Unprocessed;
    public string StatusReason { get; init; } = string.Empty;
    public string? BlockedByConnection { get; init; }

    public long SizeBytes { get; init; }
    public long? VideoWidth { get; init; }
    public long? VideoHeight { get; init; }
    public string? VideoCodec { get; init; }
    public long? AudioTrackCount { get; init; }
    public long? SubtitleTrackCount { get; init; }
    public double? DurationSeconds { get; init; }
    public string? AudioCodecs { get; init; }
    public long? VideoBitDepth { get; init; }
    public Timestamp? SizeChangedAt { get; init; }
    public Timestamp? HoldUntil { get; init; }
    public string? FailureClass { get; init; }
    public long FailureAttempts { get; init; }
    public Timestamp? NextRetryAt { get; init; }

    public string? OutputCollisionPolicy { get; init; }
    public string? OutputCollisionAction { get; init; }
    public string? OutputCollisionReason { get; init; }

    public string? HardwareMethod { get; init; }
    public bool HardwareFellBackToSoftware { get; init; }
    public string? HardwareReason { get; init; }

    public Timestamp? LastSeenAt { get; init; }
    public Timestamp? LastAttemptAt { get; init; }

    /// <summary>Size of the source the last successful pass cleaned; null before one, or before migration 0010.</summary>
    public long? ProcessedSourceSize { get; init; }

    /// <summary>Modification time (ns since the Unix epoch) of that source, as <c>SourceFiles.Fingerprint</c> measures it.</summary>
    public long? ProcessedSourceMtimeNs { get; init; }

    /// <summary>
    /// The size and modification time Weir read from the source at the moment this row most recently became
    /// <see cref="ProcessingFileStatuses.ProcessingFailed"/> or <see cref="ProcessingFileStatuses.Rejected"/> (#785).
    /// Null before migration 0025, or when the file could not be read at that moment. Activity's remove dialog compares
    /// these against the file on disk before "delete" or "keep" act, so a different release that has since landed at the
    /// same path is never mistaken for the one that actually failed.
    /// </summary>
    public long? FingerprintSizeBytes { get; init; }

    public long? FingerprintMtimeNs { get; init; }

    public Timestamp CreatedAt { get; init; }
    public Timestamp UpdatedAt { get; init; }
}

/// <summary>One page of files, and where the next one starts.</summary>
/// <param name="Rows">The files, in the order the list was asked for.</param>
/// <param name="NextCursor">The opaque cursor of the next page, or null when this one reaches the last file.</param>
public sealed record ProcessingFilePage(IReadOnlyList<ProcessingFileRecord> Rows, string? NextCursor);

/// <summary>Filters for listing <c>files</c> rows.</summary>
public sealed record ProcessingFileListFilter
{
    public long? LibraryId { get; init; }

    /// <summary>Only these statuses, when set: <c>OR</c>ed together, so the Processing screen can ask for
    /// "every file in flight" (<see cref="ProcessingFileStatuses.Processing"/> alone today) in one page with
    /// no <see cref="Limit"/>-driven risk of losing one that is still running (#781).</summary>
    public IReadOnlyList<string>? Statuses { get; init; }
    public string? PathContains { get; init; }
    public Timestamp? Since { get; init; }

    /// <summary>Only these files, when set: a person's exact choice rather than whatever else matches.</summary>
    public IReadOnlyList<long>? Ids { get; init; }
    public int Limit { get; init; } = 200;

    public ProcessingFileSort Sort { get; init; } = ProcessingFileSort.LastSeen;

    /// <summary>Which way <see cref="Sort"/> runs. Newest first is how a list has always read.</summary>
    public SortDirection Direction { get; init; } = SortDirection.Descending;

    /// <summary>The key of the file the page follows (see <c>ProcessingFileOrdering</c>), or null to start from the top.</summary>
    public IReadOnlyList<object?>? After { get; init; }

    /// <summary><see cref="Limit"/> clamped to 1..1000.</summary>
    public int ClampedLimit => Math.Max(1, Math.Min(Limit, 1000));
}

/// <summary>
/// Whether a file on disk is the one a successful pass already cleaned (migration 0010). Used only for a library that
/// keeps originals, whose sources stay in the watched folder after cleaning: without it every scan would queue the same
/// finished file again. Size and modification time together, the same two facts the pass measured before it started
/// (<c>SourceFiles.Fingerprint</c>), so a genuinely new or replaced file at the same path is processed again.
/// </summary>
public static class ProcessedSourceRules
{
    public static bool IsSameCleanedFile(ProcessingFileRecord? record, long sizeBytes, long? modifiedTimeNs) =>
        record is { Status: ProcessingFileStatuses.Processed, ProcessedSourceSize: { } size, ProcessedSourceMtimeNs: { } mtime } &&
        size == sizeBytes &&
        modifiedTimeNs == mtime;

    /// <summary>A pass recorded what it cleaned, so the fingerprint decides; older rows fall back to the pre-0010 checks.</summary>
    public static bool HasFingerprint(ProcessingFileRecord? record) =>
        record is { ProcessedSourceSize: not null, ProcessedSourceMtimeNs: not null };
}
