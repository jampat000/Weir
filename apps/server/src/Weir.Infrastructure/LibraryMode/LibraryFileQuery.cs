using Weir.Core.LibraryMode;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>One file as the #568 Files table shows it: the scan's own verdict plus the cached media facts.</summary>
public sealed record LibraryFileRow(
    long Id,
    string Path,
    long SizeBytes,
    long ModifiedTimeUnixSeconds,
    LibraryFileClassification Classification,
    string? Summary,
    string? Reason,
    int RemovedAudioCount,
    int RemovedSubtitleCount,
    long EstimatedBytesSaved,
    string? ManagerKind,
    string? ManagerTitle,
    string VideoCodec,
    int? VideoHeight,
    string ResolutionClass,
    int AudioTrackCount,
    int SubtitleTrackCount,
    string? AudioSummary,
    string? SubtitleSummary,
    int? LinkCount,
    LibraryProblemKind? ProblemKind,
    // When Weir last cleaned this file, or null when it never has, and whether a person told Weir to leave it
    // alone. Both come from library_file_marks (migration 0012), not from the scan.
    DateTimeOffset? CleanedAt = null,
    bool LeaveAlone = false);

/// <summary>The Files table's filters. Every field narrows; an empty filter is the whole library.</summary>
public sealed record LibraryFileQuery
{
    public string? Classification { get; init; }

    public string? ManagerKind { get; init; }

    /// <summary>A case-insensitive substring of the path or the manager's title.</summary>
    public string? Search { get; init; }

    /// <summary>Facet constraints, AND'ed: a file must carry every one of these <c>(facet, value)</c> pairs.</summary>
    public IReadOnlyList<LibraryFileFacet> Facets { get; init; } = [];

    public LibraryProblemKind? ProblemKind { get; init; }

    /// <summary><c>cleaned</c> or <c>left_alone</c>: what Weir has done with the file, rather than what is in it.</summary>
    public string? State { get; init; }

    /// <summary>A sort key from <see cref="LibraryFileSort.Columns"/>; anything else falls back to the path.</summary>
    public string Sort { get; init; } = LibraryFileSort.Path;

    public bool Descending { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

/// <summary>The closed set of sortable columns. Nothing outside this map ever reaches the SQL.</summary>
public static class LibraryFileSort
{
    public const string Path = "path";

    /// <summary>Column expression per accepted sort key. The key is the wire name; the value is checked-in SQL.</summary>
    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["path"] = "f.path",
        ["title"] = "COALESCE(f.manager_title, f.path)",
        ["size"] = "f.size_bytes",
        ["state"] = "f.classification",
        ["saved"] = "f.estimated_bytes_saved",
        ["video"] = "f.video_codec",
        ["resolution"] = "COALESCE(f.video_height, 0)",
        ["audio"] = "f.audio_track_count",
        ["subtitles"] = "f.subtitle_track_count",
        ["modified"] = "f.mtime",
    };

    public static string ColumnFor(string? sort) =>
        sort is not null && Columns.TryGetValue(sort, out var column) ? column : Columns[Path];

    public static string Normalize(string? sort) => sort is not null && Columns.ContainsKey(sort) ? sort : Path;

    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;
}
