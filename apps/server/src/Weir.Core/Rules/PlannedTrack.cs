namespace Weir.Core.Rules;

public enum TrackKind
{
    Audio,
    Subtitle,
}

/// <summary>One kept track in a plan.</summary>
public sealed record PlannedTrack
{
    public required int InputIndex { get; init; }
    public required string LangLabel { get; init; }
    public bool Commentary { get; init; }
    public bool Forced { get; init; }
    public bool Default { get; init; }
    public int Channels { get; init; }
    public bool Lossless { get; init; }
    public long Bitrate { get; init; }
    public int CodecRank { get; init; } = RemuxRules.CodecUnknownRank;
    public string CodecName { get; init; } = string.Empty;
    public TrackKind Kind { get; init; } = TrackKind.Audio;

    /// <summary>
    /// Issue #496: the regional/script variant detected for this track (<c>"fre-CA"</c>), or null
    /// when none was detected or the base language rule matched regardless of variant.
    /// </summary>
    public string? Variant { get; init; }
}
