namespace Weir.Core.Rules;

/// <summary>
/// The facts a sorter can look at for one track. The planner fills <see cref="Title"/> with the
/// stream's own title tag (#537 item 2).
/// </summary>
public sealed record SortableTrack
{
    public int Index { get; init; }
    public string Language { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool Commentary { get; init; }
    public bool Default { get; init; }
    public bool Forced { get; init; }
    public long Channels { get; init; }
    public long Bitrate { get; init; }
    public string Codec { get; init; } = string.Empty;
    public long CodecRank { get; init; }

    /// <summary>
    /// Issue #497: whether <see cref="TrackFlagsReader"/> detected this track as a dub or an audio
    /// description, for the <c>content_tier</c> field (main &gt; dub/audio description &gt; commentary).
    /// </summary>
    public bool Dub { get; init; }

    public bool AudioDescription { get; init; }

    /// <summary>The fact named <paramref name="field"/>: a bool, a long, a string, or null for a key the track does not have.</summary>
    internal object? Get(string field) => field switch
    {
        "index" => (long)Index,
        "language" => Language,
        "title" => Title,
        "commentary" => Commentary,
        "default" => Default,
        "forced" => Forced,
        "channels" => Channels,
        "bitrate" => Bitrate,
        "codec" => Codec,
        "codec_rank" => CodecRank,
        "dub" => Dub,
        "audio_description" => AudioDescription,
        "content_tier" => Commentary ? "commentary" : Dub || AudioDescription ? "dub" : "main",
        _ => null,
    };
}
