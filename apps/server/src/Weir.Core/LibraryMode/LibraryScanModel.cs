using Weir.Core.Json;

namespace Weir.Core.LibraryMode;

/// <summary>One file as a scan (#505 point 2) last saw it: its classification, and the raw ffprobe JSON cached by path, size
/// and mtime, so a later scan reuses it instead of re-probing an unchanged file.
///
/// <para><see cref="ManagerConnectionId"/>, <see cref="ManagerTitleId"/>, <see cref="ManagerFileId"/> and
/// <see cref="ManagerQualityProfileId"/> (issue #551) are the manager-side identity a scan matched this file to,
/// via <c>IMediaManagerPort.ListLibraryFilesAsync</c> (#507) and a reverse path mapping — populated together
/// with <see cref="ManagerKind"/>/<see cref="ManagerTitle"/>, and null together with them for an unmatched file
/// (still processable: #505 explicitly allows this). They feed #508's re-download-risk preflight and #509's
/// redownload endpoints, which could not act on a matched title without them.</para></summary>
public sealed record LibraryScanFileEntry(
    string Path,
    long SizeBytes,
    long ModifiedTimeUnixSeconds,
    LibraryFileClassification Classification,
    string? Summary,
    string? Reason,
    int RemovedAudioCount,
    int RemovedSubtitleCount,
    string? ManagerKind,
    string? ManagerTitle,
    string? ProbeJson,
    long EstimatedBytesSaved = 0,
    long? ManagerConnectionId = null,
    string? ManagerTitleId = null,
    long? ManagerFileId = null,
    long? ManagerQualityProfileId = null,
    LibraryProblemKind? ProblemKind = null,
    int? LinkCount = null)
{
    /// <summary>Whether a freshly-walked file is the same one this entry already probed (path, size and mtime all agree).</summary>
    public bool MatchesFile(string path, long sizeBytes, long modifiedTimeUnixSeconds) =>
        string.Equals(Path, path, StringComparison.Ordinal) && SizeBytes == sizeBytes && ModifiedTimeUnixSeconds == modifiedTimeUnixSeconds;

    public WireObject ToPyDict() => new WireObject()
        .Set("path", Path)
        .Set("size_bytes", SizeBytes)
        .Set("mtime", ModifiedTimeUnixSeconds)
        .Set("classification", ClassificationName(Classification))
        .Set("summary", Summary)
        .Set("reason", Reason)
        .Set("removed_audio_tracks", RemovedAudioCount)
        .Set("removed_subtitle_tracks", RemovedSubtitleCount)
        .Set("manager_kind", ManagerKind)
        .Set("manager_title", ManagerTitle)
        .Set("probe_json", ProbeJson)
        .Set("estimated_bytes_saved", EstimatedBytesSaved)
        .Set("manager_connection_id", ManagerConnectionId)
        .Set("manager_title_id", ManagerTitleId)
        .Set("manager_file_id", ManagerFileId)
        .Set("manager_quality_profile_id", ManagerQualityProfileId)
        .Set("problem_kind", ProblemKind is { } kind ? LibraryProblems.Name(kind) : null)
        .Set("link_count", LinkCount);

    public static LibraryScanFileEntry? FromPyDict(WireValue value)
    {
        if (value is not WireObject dict || dict.Get("path") is not WireString path || path.Value.Length == 0)
        {
            return null;
        }

        return new LibraryScanFileEntry(
            path.Value,
            LongOf(dict.Get("size_bytes")),
            LongOf(dict.Get("mtime")),
            ClassificationOf(dict.Get("classification")),
            StrOrNull(dict.Get("summary")),
            StrOrNull(dict.Get("reason")),
            (int)LongOf(dict.Get("removed_audio_tracks")),
            (int)LongOf(dict.Get("removed_subtitle_tracks")),
            StrOrNull(dict.Get("manager_kind")),
            StrOrNull(dict.Get("manager_title")),
            StrOrNull(dict.Get("probe_json")),
            LongOf(dict.Get("estimated_bytes_saved")),
            LongOrNull(dict.Get("manager_connection_id")),
            StrOrNull(dict.Get("manager_title_id")),
            LongOrNull(dict.Get("manager_file_id")),
            LongOrNull(dict.Get("manager_quality_profile_id")),
            LibraryProblems.Parse(StrOrNull(dict.Get("problem_kind"))),
            LongOrNull(dict.Get("link_count")) is { } links ? (int)links : null);
    }

    public static string ClassificationName(LibraryFileClassification classification) => classification switch
    {
        LibraryFileClassification.Matches => "matches",
        LibraryFileClassification.WouldChange => "would_change",
        _ => "cannot_process",
    };

    private static LibraryFileClassification ClassificationOf(WireValue? value) => value is WireString { Value: var text } ? text switch
    {
        "matches" => LibraryFileClassification.Matches,
        "would_change" => LibraryFileClassification.WouldChange,
        _ => LibraryFileClassification.CannotProcess,
    }
    : LibraryFileClassification.CannotProcess;

    private static long LongOf(WireValue? value) => value is WireInteger i ? (long)i.Value : 0;

    private static long? LongOrNull(WireValue? value) => value is WireInteger i ? (long)i.Value : null;

    private static string? StrOrNull(WireValue? value) => value is WireString s ? s.Value : null;
}

/// <summary>The result of one completed scan (#505 point 2): the file index / plan cache for a library, embedded in the
/// completed scan job's <c>payload_json</c> under <c>scan_result</c> rather than a new table.</summary>
public sealed record LibraryScanSnapshot(long LibraryId, DateTimeOffset GeneratedAt, IReadOnlyList<LibraryScanFileEntry> Files, IReadOnlyList<string> Errors)
{
    public const string PayloadKey = "scan_result";

    public WireObject ToPyDict() => new WireObject()
        .Set("generated_at", GeneratedAt.ToUnixTimeSeconds())
        .Set("files", new WireArray(Files.Select(f => (WireValue)f.ToPyDict())))
        .Set("errors", new WireArray(Errors.Select(e => (WireValue)new WireString(e))));

    public static LibraryScanSnapshot? FromPayload(WireObject payload, long libraryId)
    {
        if (payload.Get(PayloadKey) is not WireObject scan)
        {
            return null;
        }

        var generatedAt = scan.Get("generated_at") is WireInteger seconds
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds.Value)
            : DateTimeOffset.UnixEpoch;
        var files = scan.Get("files") is WireArray list
            ? list.Items.Select(LibraryScanFileEntry.FromPyDict).OfType<LibraryScanFileEntry>().ToList()
            : [];
        var errors = scan.Get("errors") is WireArray errorList
            ? errorList.Items.OfType<WireString>().Select(s => s.Value).ToList()
            : [];
        return new LibraryScanSnapshot(libraryId, generatedAt, files, errors);
    }
}
