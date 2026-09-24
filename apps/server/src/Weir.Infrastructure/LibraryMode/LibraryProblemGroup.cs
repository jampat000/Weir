using Weir.Core.LibraryMode;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>Totals for a whole library, or for the subset a filter selects.</summary>
public sealed record LibraryTotals(
    long Files,
    long SizeBytes,
    long Matches,
    long WouldChange,
    long CannotProcess,
    long EstimatedBytesSaved,
    long RemovedAudioTracks,
    long RemovedSubtitleTracks,
    // Files Weir has cleaned at least once, and files it has been told to leave alone (migration 0012).
    long Cleaned = 0,
    long LeftAlone = 0);

/// <summary>One row of a breakdown: how many files carry this value, and how much disk they take between them.</summary>
public sealed record LibraryBreakdownRow(string Value, long Files, long SizeBytes);

/// <summary>One Problems group: the kind, how many files, and a few paths to show without paging the whole group.</summary>
public sealed record LibraryProblemGroup(LibraryProblemKind Kind, long Files, long SizeBytes, IReadOnlyList<string> SampleFiles);
