using Weir.Core.Rules;

namespace Weir.Core.Tests.Rules;

/// <summary>
/// A profile's "Subtitle order" orders the subtitle tracks Weir keeps (James, 23 Sep 2026: make the settings that did
/// nothing work). It was saved on every profile and never read. With no order saved, the order is today's.
/// </summary>
public sealed class SubtitleOrderTests
{
    private static ProbeStreamInfo Stream(string json) => ProbeStreamInfo.Parse(json);

    private static readonly ProbeStreamInfo Video = Stream("""{"index": 0, "codec_type": "video", "codec_name": "h264"}""");
    private static readonly ProbeStreamInfo English = Stream("""{"index": 1, "codec_type": "audio", "codec_name": "aac", "channels": 2, "tags": {"language": "eng"}}""");

    private static ProbeStreamInfo Subtitle(int index, string language, bool forced = false, bool isDefault = false) =>
        Stream($$"""{"index": {{index}}, "codec_type": "subtitle", "codec_name": "subrip", "disposition": {"forced": {{(forced ? 1 : 0)}}, "default": {{(isDefault ? 1 : 0)}}}, "tags": {"language": "{{language}}"} }""");

    /// <summary>A file whose subtitles run Japanese, English, then an English forced track.</summary>
    private static readonly ProbeStreamInfo[] Subtitles = [Subtitle(2, "jpn"), Subtitle(3, "eng"), Subtitle(4, "eng", forced: true)];

    private static ProcessingRulesConfig KeepSelected(string order) => RemuxRules.DefaultConfig() with
    {
        SubtitleMode = RemuxRuleValues.SubtitleModeKeepSelected,
        SubtitleLangs = ["eng", "jpn"],
        PreserveForcedSubs = true,
        SubtitleSortersJson = order,
    };

    [Fact]
    public void With_no_order_saved_the_languages_to_keep_list_decides_as_before()
    {
        var plan = RemuxRules.PlanRemux([Video], [English], Subtitles, KeepSelected(string.Empty));

        Assert.NotNull(plan);
        Assert.Equal([3, 4, 2], plan!.Subtitles.Select(t => t.InputIndex));
    }

    [Fact]
    public void Forced_then_default_then_language_puts_the_forced_track_first_and_keeps_the_list_order_after_it()
    {
        const string forcedDefaultLanguage =
            """[{"field":"forced","value":"","reversed":false},{"field":"default","value":"","reversed":false},{"field":"language","value":"","reversed":false}]""";

        var plan = RemuxRules.PlanRemux([Video], [English], Subtitles, KeepSelected(forcedDefaultLanguage));

        Assert.NotNull(plan);
        // The forced English track leads; then English before Japanese, as the languages-to-keep list has them, not as
        // the alphabet would.
        Assert.Equal([4, 3, 2], plan!.Subtitles.Select(t => t.InputIndex));
        Assert.Contains(plan.AudioSelectionNotes, note => note.StartsWith("Subtitle order:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reversed_language_criterion_puts_the_last_listed_language_first()
    {
        const string languageReversed = """[{"field":"language","value":"","reversed":true}]""";

        var plan = RemuxRules.PlanRemux([Video], [English], Subtitles, KeepSelected(languageReversed));

        Assert.NotNull(plan);
        Assert.Equal([2, 3, 4], plan!.Subtitles.Select(t => t.InputIndex));
    }
}
