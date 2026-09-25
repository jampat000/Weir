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

    /// <summary>Terminal: under the opt-in <c>reject</c> policy, the manager accepted that the release is bad.</summary>
    public const string Rejected = "rejected";

    /// <summary>Terminal: someone cancelled the file's queued pass before Weir started on it, from the Jobs screen or through
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

/// <summary>What a cancelled file says on the Files screen (#643).</summary>
public static class CancelledFileReasons
{
    public const string InWeir =
        "Cancelled from the Jobs screen before Weir started on it. The original is untouched; queue it again from Files to process it.";

    public const string ByManager =
        "The media manager cancelled this hand-off before Weir started on it. The original is untouched; queue it again from Files to process it.";
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

    public Timestamp CreatedAt { get; init; }
    public Timestamp UpdatedAt { get; init; }
}

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
