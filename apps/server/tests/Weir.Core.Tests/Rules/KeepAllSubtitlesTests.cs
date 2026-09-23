using Weir.Core.Library;
using Weir.Core.Rules;

namespace Weir.Core.Tests.Rules;

/// <summary>
/// "Keep all subtitles" (<c>keep_all</c>, the stored default) keeps every subtitle track. Read as keep-selected, it would
/// remove every subtitle, forced and default tracks included, when the language list the Rules screen hides in that mode
/// is empty, and keep only the listed languages when a list is left over from an earlier choice.
/// </summary>
public sealed class KeepAllSubtitlesTests
{
    private static ProbeStreamInfo Stream(string json) => ProbeStreamInfo.Parse(json);

    private static readonly ProbeStreamInfo Video = Stream("""{"index": 0, "codec_type": "video", "codec_name": "h264"}""");
    private static readonly ProbeStreamInfo English = Stream("""{"index": 1, "codec_type": "audio", "codec_name": "aac", "channels": 2, "tags": {"language": "eng"}}""");

    private static ProbeStreamInfo Subtitle(int index, string? language, string codec = "subrip", bool forced = false, bool isDefault = false, string? title = null)
    {
        var tags = new List<string>();
        if (language is not null)
        {
            tags.Add($"\"language\": \"{language}\"");
        }

        if (title is not null)
        {
            tags.Add($"\"title\": \"{title}\"");
        }

        return Stream($$"""{"index": {{index}}, "codec_type": "subtitle", "codec_name": "{{codec}}", "disposition": {"forced": {{(forced ? 1 : 0)}}, "default": {{(isDefault ? 1 : 0)}}}, "tags": { {{string.Join(", ", tags)}} } }""");
    }

    private static ProcessingRulesConfig KeepAll(params string[] leftoverLanguages) => RemuxRules.DefaultConfig() with
    {
        SubtitleMode = RemuxRuleValues.SubtitleModeKeepAll,
        SubtitleLangs = leftoverLanguages,
        PreserveForcedSubs = true,
        PreserveDefaultSubs = true,
    };

    [Fact]
    public void Keeps_every_subtitle_with_no_language_list_including_forced_default_and_untagged_tracks()
    {
        var subtitles = new[]
        {
            Subtitle(2, "eng", isDefault: true),
            Subtitle(3, "fre", forced: true),
            Subtitle(4, "jpn"),
            Subtitle(5, null),
        };

        var plan = RemuxRules.PlanRemux([Video], [English], subtitles, KeepAll());

        Assert.NotNull(plan);
        Assert.Equal([2, 3, 4, 5], plan!.Subtitles.Select(t => t.InputIndex));
        Assert.Empty(plan.RemovedSubtitles);
        Assert.True(plan.Subtitles.Single(t => t.InputIndex == 3).Forced);
        Assert.True(plan.Subtitles.Single(t => t.InputIndex == 2).Default);
    }

    [Fact]
    public void A_language_list_left_over_from_keep_selected_does_not_narrow_keep_all()
    {
        var subtitles = new[] { Subtitle(2, "eng"), Subtitle(3, "spa"), Subtitle(4, "ger") };

        var plan = RemuxRules.PlanRemux([Video], [English], subtitles, KeepAll("eng"));

        Assert.NotNull(plan);
        Assert.Equal([2, 3, 4], plan!.Subtitles.Select(t => t.InputIndex));
        Assert.Empty(plan.RemovedSubtitles);
    }

    [Fact]
    public void Still_removes_hearing_impaired_tracks_when_that_rule_is_on()
    {
        var subtitles = new[] { Subtitle(2, "eng"), Subtitle(3, "eng", title: "English (SDH)") };
        var config = KeepAll() with { RemoveHearingImpairedSubs = true };

        var plan = RemuxRules.PlanRemux([Video], [English], subtitles, config);

        Assert.NotNull(plan);
        Assert.Equal([2], plan!.Subtitles.Select(t => t.InputIndex));
        Assert.Contains(plan.RemovedTrackRecords, r => r.Type == RemovedTrackType.Subtitle && r.Reason == "hearing-impaired subtitle removed");
    }

    [Fact]
    public void The_per_language_cap_applies_to_each_language_the_file_has()
    {
        var subtitles = new[]
        {
            Subtitle(2, "eng", codec: "hdmv_pgs_subtitle"),
            Subtitle(3, "eng", codec: "subrip"),
            Subtitle(4, "fre", codec: "subrip"),
            Subtitle(5, "fre", codec: "hdmv_pgs_subtitle"),
            Subtitle(6, "jpn", codec: "subrip"),
        };
        var config = KeepAll() with { SubtitleMaxPerLanguage = 1, SubtitleQualityStrategy = RemuxRuleValues.SubtitleStrategyTextFirst };

        var plan = RemuxRules.PlanRemux([Video], [English], subtitles, config);

        Assert.NotNull(plan);
        // One of each language, the text track where there is a choice, in the file's own order.
        Assert.Equal([3, 4, 6], plan!.Subtitles.Select(t => t.InputIndex));
    }

    [Fact]
    public void Remove_all_and_an_empty_keep_selected_list_still_remove_every_subtitle()
    {
        var subtitles = new[] { Subtitle(2, "eng"), Subtitle(3, "fre", forced: true) };

        var removeAll = RemuxRules.PlanRemux([Video], [English], subtitles, KeepAll() with { SubtitleMode = RemuxRuleValues.SubtitleModeRemoveAll });
        var emptySelection = RemuxRules.PlanRemux([Video], [English], subtitles, KeepAll() with { SubtitleMode = RemuxRuleValues.SubtitleModeKeepSelected });

        Assert.Empty(removeAll!.Subtitles);
        Assert.Empty(emptySelection!.Subtitles);
    }

    [Fact]
    public void The_per_track_explanation_agrees_with_the_plan()
    {
        var subtitles = new[] { Subtitle(2, "eng"), Subtitle(3, null) };

        var decisions = RemuxRules.ExplainTracks([Video], [English], subtitles, KeepAll());

        var subtitleDecisions = decisions.Where(d => d.Kind == "subtitle").ToList();
        Assert.Equal(2, subtitleDecisions.Count);
        Assert.All(subtitleDecisions, d =>
        {
            Assert.True(d.WouldKeep);
            Assert.Equal("Kept: the saved rules keep every subtitle language.", d.Reason);
        });
    }

    [Fact]
    public void A_subtitle_removed_earlier_would_now_be_kept()
    {
        var removed = new RemovedTrackRecord { Language = "spa", Type = RemovedTrackType.Subtitle };

        Assert.True(RemovedTrackDiff.WouldNowBeKept(KeepAll(), removed));
        Assert.False(RemovedTrackDiff.WouldNowBeKept(KeepAll() with { SubtitleMode = RemuxRuleValues.SubtitleModeRemoveAll }, removed));
    }
}
