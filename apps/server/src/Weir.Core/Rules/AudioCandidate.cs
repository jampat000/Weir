namespace Weir.Core.Rules;

public static partial class RemuxRules
{
    internal sealed record AudioCandidate(
        int InputIndex,
        string LangLabel,
        string Title,
        bool Commentary,
        bool Default,
        int Channels,
        long Bitrate,
        int CodecRank,
        string CodecName,
        TrackFlags Flags,
        VariantDetection Variant);

    /// <summary>
    /// Issue #537 item 6: <c>bit_rate</c> text ffprobe cannot parse (<c>"N/A"</c> is a real value it
    /// emits for an unknown rate) is treated as unknown rather than failing the whole plan.
    /// </summary>
    private static long ReadBitRate(ProbeStreamInfo s)
    {
        try
        {
            return RulesJson.Truthy(s.Get("bit_rate")) ? RulesJson.Int(s.Get("bit_rate")) : 0;
        }
        catch (RulesInputException)
        {
            return 0;
        }
    }

    private static AudioCandidate CandidateFromStream(ProbeStreamInfo s, int index)
    {
        var tags = s.Tags;
        var rawLanguageTag = tags.GetValueOrDefault("language");
        var lang = NormalizeLang(rawLanguageTag);
        var disposition = s.Disposition;
        var codecName = RulesJson.StrOr(s.Get("codec_name"), string.Empty);
        var channels = RulesJson.ToInt32(RulesJson.Truthy(s.Get("channels")) ? RulesJson.Int(s.Get("channels")) : 0);
        var bitrate = ReadBitRate(s);
        var flags = TrackFlagsReader.Detect(s);
        // Issue #537 item 5: a non-string title tag (a list, say) is missing, not stringified.
        var title = tags.GetValueOrDefault("title") ?? string.Empty;
        return new AudioCandidate(
            InputIndex: index,
            LangLabel: lang,
            Title: title,
            Commentary: flags.Commentary.Value,
            Default: disposition.GetValueOrDefault("default") != 0,
            Channels: channels,
            Bitrate: bitrate,
            CodecRank: AudioCodecQualityRank(codecName),
            CodecName: codecName.Length > 0 ? codecName : "unknown",
            Flags: flags,
            // Issue #496: regional/script variant from the track's name or an explicit BCP 47 tag.
            Variant: LanguageVariants.Detect(title, lang, rawLanguageTag));
    }

    private static SortableTrack CandidateAsTrack(AudioCandidate c) => new()
    {
        Index = c.InputIndex,
        Language = c.LangLabel,
        // Issue #537 item 2: a "title" sorter compares the stream's own title tag, not the codec name, so
        // "demote a title containing X" can match a real track. golden/overrides patches the golden files
        // that recorded the codec name here.
        Title = c.Title,
        Commentary = c.Commentary,
        Default = c.Default,
        Forced = false,
        Channels = c.Channels,
        Bitrate = c.Bitrate,
        Codec = c.CodecName,
        CodecRank = c.CodecRank,
        // Issue #497: feeds the default sorters' content_tier key.
        Dub = c.Flags.Dub.Value,
        AudioDescription = c.Flags.AudioDescription.Value,
    };

    /// <summary>#509's removed-track records never leave <see cref="RemovedTrackRecord.Codec"/> blank.</summary>
    private static string CodecOrUnknown(string codecName) => codecName.Length > 0 ? codecName : "unknown";

    private static string DescribeCandidate(AudioCandidate c)
    {
        var lang = c.LangLabel.Length > 0 ? c.LangLabel : "unknown";
        var channels = c.Channels > 0 ? $"{c.Channels} ch" : "unknown channels";
        return $"{lang} {c.CodecName} {channels} (stream {c.InputIndex})";
    }
}
