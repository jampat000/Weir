namespace Weir.Core.Rules;

/// <summary>What one pass writes.</summary>
public sealed record RemuxPlan
{
    public required IReadOnlyList<int> VideoIndices { get; init; }
    public required IReadOnlyList<PlannedTrack> Audio { get; init; }
    public required IReadOnlyList<PlannedTrack> Subtitles { get; init; }
    public IReadOnlyList<string> RemovedAudio { get; init; } = [];
    public IReadOnlyList<string> RemovedSubtitles { get; init; } = [];

    /// <summary>
    /// The same removals as <see cref="RemovedAudio"/>/<see cref="RemovedSubtitles"/>, structured for #509
    /// (a rule change surfacing titles that can only be fixed by re-downloading): one entry per removed
    /// track with its language, kind and codec, captured from the same source data those display strings
    /// are built from rather than parsed back out of them. The golden files compare only the fields
    /// <c>GoldenParityTests.WritePlanResult</c> names, which does not include this one.
    /// </summary>
    public IReadOnlyList<RemovedTrackRecord> RemovedTrackRecords { get; init; } = [];
    public int DefaultAudioOutputIndex { get; init; }
    public IReadOnlyList<string> AudioSelectionNotes { get; init; } = [];

    /// <summary>Embedded posters this plan drops, described.</summary>
    public IReadOnlyList<string> RemovedImages { get; init; } = [];

    public IReadOnlyList<string> RemovedAttachments { get; init; } = [];
    public IReadOnlyList<string> MetadataNotes { get; init; } = [];
    public MetadataRules Metadata { get; init; } = new();
}

/// <summary>The streams of a probe by type, each ordered by index.</summary>
public sealed record SplitProbeStreams(IReadOnlyList<ProbeStreamInfo> Video, IReadOnlyList<ProbeStreamInfo> Audio, IReadOnlyList<ProbeStreamInfo> Subtitles);
