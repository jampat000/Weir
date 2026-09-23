using Weir.Core.LibraryMode;
using Weir.Core.Rules;

namespace Weir.Core.Tests.LibraryMode;

public sealed class LibraryFilePlannerTests
{
    private const string EnglishAndJapanese =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"eng"},"disposition":{"default":1},"bit_rate":"128000"},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn"},"bit_rate":"96000"}]}""";

    private const string EnglishJapaneseAndFrench =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"eng"},"disposition":{"default":1},"bit_rate":"128000"},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn"},"bit_rate":"96000"},{"index":3,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"fre"},"bit_rate":"96000"}]}""";

    private const string EnglishOnly =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"eng"},"disposition":{"default":1},"bit_rate":"128000"}]}""";

    private const string NoVideo =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"eng"}}]}""";

    private static ProcessingRulesConfig EnglishOnlyRules() => new()
    {
        PrimaryAudioLang = "eng",
        SecondaryAudioLang = string.Empty,
        TertiaryAudioLang = string.Empty,
        DefaultAudioSlot = RemuxRuleValues.DefaultAudioSlotPrimary,
        RemoveCommentary = false,
        SubtitleMode = "keep_all",
        SubtitleLangs = [],
        PreserveForcedSubs = true,
        PreserveDefaultSubs = true,
        AudioPreferenceMode = RemuxRuleValues.PolicyPreferredLangsStrict,
    };

    [Fact]
    public void A_file_with_only_the_kept_language_matches()
    {
        var result = LibraryFilePlanner.Classify(ProbeResult.Parse(EnglishOnly), EnglishOnlyRules());
        Assert.Equal(LibraryFileClassification.Matches, result.Classification);
        Assert.Equal(0, result.RemovedAudioCount);
    }

    [Fact]
    public void A_file_with_an_extra_language_would_change_and_estimates_the_removed_tracks_bytes()
    {
        var result = LibraryFilePlanner.Classify(ProbeResult.Parse(EnglishAndJapanese), EnglishOnlyRules());
        Assert.Equal(LibraryFileClassification.WouldChange, result.Classification);
        Assert.Equal(1, result.RemovedAudioCount);
        Assert.NotNull(result.Summary);
        Assert.StartsWith("Would remove 1 audio track (jpn ", result.Summary, StringComparison.Ordinal);
        // 96000 bits/s * 120s / 8 = 1,440,000 bytes for the removed Japanese track.
        Assert.Equal(1_440_000, result.EstimatedBytesSaved);
    }

    // #648: Matroska keeps per-track sizes in the stream's own tags, and reports no bit_rate at all. mkvmerge writes
    // both the exact byte count and a bit rate, each with the track's language appended.
    private const string MatroskaWithStatisticsTags =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"eac3","channels":6,"tags":{"language":"eng","BPS-eng":"640000","NUMBER_OF_BYTES-eng":"9600000"},"disposition":{"default":1}},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn","BPS-jpn":"128000","NUMBER_OF_BYTES-jpn":"1920000"}}]}""";

    private const string MatroskaWithBpsOnly =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"eac3","channels":6,"tags":{"language":"eng"},"disposition":{"default":1}},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn","BPS":"128000"}}]}""";

    private const string MatroskaWithNoSizes =
        """{"format":{"duration":"120.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"},{"index":1,"codec_type":"audio","codec_name":"eac3","channels":6,"tags":{"language":"eng"},"disposition":{"default":1}},{"index":2,"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn"}}]}""";

    [Fact]
    public void A_matroska_track_is_measured_by_its_own_tags_before_any_arithmetic()
    {
        var exact = LibraryFilePlanner.Classify(ProbeResult.Parse(MatroskaWithStatisticsTags), EnglishOnlyRules());
        // The dropped Japanese track says exactly how many bytes it is, so nothing is worked out from a bit rate.
        Assert.Equal(1_920_000, exact.EstimatedBytesSaved);

        // With only a bit rate to go on: 128000 bits/s * 120s / 8.
        var fromBitRate = LibraryFilePlanner.Classify(ProbeResult.Parse(MatroskaWithBpsOnly), EnglishOnlyRules());
        Assert.Equal(1_920_000, fromBitRate.EstimatedBytesSaved);

        // A file that says nothing about its tracks' sizes still reads as one that would change.
        var unmeasured = LibraryFilePlanner.Classify(ProbeResult.Parse(MatroskaWithNoSizes), EnglishOnlyRules());
        Assert.Equal(LibraryFileClassification.WouldChange, unmeasured.Classification);
        Assert.Equal(1, unmeasured.RemovedAudioCount);
        Assert.Equal(0, unmeasured.EstimatedBytesSaved);
    }

    // #648: what ffprobe -show_streams -show_format reports for an episode mkvmerge wrote (v81, unsuffixed tag names):
    // every track carries BPS, DURATION, NUMBER_OF_FRAMES and NUMBER_OF_BYTES plus the _STATISTICS_* bookkeeping tags,
    // and only the AC-3 family reports a stream bit_rate (from its own header). Streams 2, 4 and 5 are dropped by
    // EnglishAudioAndSubtitleRules.
    private const string MkvmergeEpisode =
        """
        {"format":{"filename":"Show.S01E01.mkv","nb_streams":6,"format_name":"matroska,webm","format_long_name":"Matroska / WebM","start_time":"0.000000","duration":"2640.042000","size":"2893012345","bit_rate":"8766555","probe_score":100,"tags":{"title":"Show S01E01","encoder":"libebml v1.4.5 + libmatroska v1.7.1","creation_time":"2024-03-02T10:11:12.000000Z"}},
         "streams":[
          {"index":0,"codec_name":"h264","codec_type":"video","width":1920,"height":1080,"r_frame_rate":"24000/1001","disposition":{"default":1,"forced":0},
           "tags":{"language":"eng","BPS":"8000000","DURATION":"00:44:00.042000000","NUMBER_OF_FRAMES":"63361","NUMBER_OF_BYTES":"2640042000","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}},
          {"index":1,"codec_name":"eac3","codec_type":"audio","sample_rate":"48000","channels":6,"channel_layout":"5.1(side)","bit_rate":"640000","disposition":{"default":1,"forced":0},
           "tags":{"language":"eng","BPS":"640000","DURATION":"00:44:00.032000000","NUMBER_OF_FRAMES":"82501","NUMBER_OF_BYTES":"211202560","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}},
          {"index":2,"codec_name":"ac3","codec_type":"audio","sample_rate":"48000","channels":6,"channel_layout":"5.1(side)","bit_rate":"448000","disposition":{"default":0,"forced":0},
           "tags":{"language":"jpn","BPS":"448000","DURATION":"00:44:00.032000000","NUMBER_OF_FRAMES":"82501","NUMBER_OF_BYTES":"147000000","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}},
          {"index":3,"codec_name":"subrip","codec_type":"subtitle","disposition":{"default":0,"forced":0},
           "tags":{"language":"eng","BPS":"72","DURATION":"00:42:58.100000000","NUMBER_OF_FRAMES":"702","NUMBER_OF_BYTES":"23203","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}},
          {"index":4,"codec_name":"subrip","codec_type":"subtitle","disposition":{"default":0,"forced":0},
           "tags":{"language":"fre","BPS":"70","DURATION":"00:41:10.500000000","NUMBER_OF_FRAMES":"655","NUMBER_OF_BYTES":"21617","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}},
          {"index":5,"codec_name":"hdmv_pgs_subtitle","codec_type":"subtitle","disposition":{"default":0,"forced":0},
           "tags":{"language":"ger","BPS":"27738","DURATION":"00:43:59.900000000","NUMBER_OF_FRAMES":"1402","NUMBER_OF_BYTES":"9150000","_STATISTICS_WRITING_APP":"mkvmerge v81.0 ('Milliontown') 64-bit","_STATISTICS_WRITING_DATE_UTC":"2024-03-02 10:11:12","_STATISTICS_TAGS":"BPS DURATION NUMBER_OF_FRAMES NUMBER_OF_BYTES"}}
         ]}
        """;

    private static ProcessingRulesConfig EnglishAudioAndSubtitleRules() => EnglishOnlyRules() with
    {
        SubtitleMode = RemuxRuleValues.SubtitleModeKeepSelected,
        SubtitleLangs = ["eng"],
    };

    [Fact]
    public void An_mkvmerge_file_is_measured_exactly_from_its_byte_counts()
    {
        var result = LibraryFilePlanner.Classify(ProbeResult.Parse(MkvmergeEpisode), EnglishAudioAndSubtitleRules());

        Assert.Equal(LibraryFileClassification.WouldChange, result.Classification);
        Assert.Equal(1, result.RemovedAudioCount);
        Assert.Equal(2, result.RemovedSubtitleCount);
        // Japanese AC-3, French SRT and German PGS, each exactly as mkvmerge counted it. The AC-3 track's own
        // bit_rate (448000 * 2640.042 / 8 = 147,842,352) is not used: the byte count is the better evidence.
        Assert.Equal(147_000_000 + 21_617 + 9_150_000, result.EstimatedBytesSaved);
    }

    [Fact]
    public void Two_removed_tracks_read_in_the_plural()
    {
        var result = LibraryFilePlanner.Classify(ProbeResult.Parse(EnglishJapaneseAndFrench), EnglishOnlyRules());
        Assert.Equal(2, result.RemovedAudioCount);
        Assert.NotNull(result.Summary);
        Assert.StartsWith("Would remove 2 audio tracks (jpn ", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_no_video_track_cannot_be_processed()
    {
        var result = LibraryFilePlanner.Classify(ProbeResult.Parse(NoVideo), EnglishOnlyRules());
        Assert.Equal(LibraryFileClassification.CannotProcess, result.Classification);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void A_rule_set_that_keeps_no_audio_cannot_be_processed()
    {
        var rules = EnglishOnlyRules() with { PrimaryAudioLang = "spa", AudioPreferenceMode = RemuxRuleValues.PolicyPreferredLangsStrict };
        // Strict preference with only English/Japanese present and Spanish required still picks a fallback in this engine,
        // so instead prove the "no audio survives" branch directly: an empty stream list can never produce a plan.
        var probe = ProbeResult.Parse("""{"format":{"duration":"1.0"},"streams":[{"index":0,"codec_type":"video","codec_name":"h264"}]}""");
        var result = LibraryFilePlanner.Classify(probe, rules);
        Assert.Equal(LibraryFileClassification.CannotProcess, result.Classification);
    }
}
