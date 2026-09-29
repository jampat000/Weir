using Weir.Core.Rules;

namespace Weir.Core.Tests.Rules;

/// <summary>
/// The sentence a person reads when the audio rules leave a file with nothing to keep. Each case is one the planner returns
/// no plan for, so what is said matches the rule that actually decided.
/// </summary>
public sealed class AudioRejectionExplanationTests
{
    private static readonly ProbeStreamInfo Video = ProbeStreamInfo.Parse("""{"index": 0, "codec_type": "video", "codec_name": "h264"}""");

    private static ProbeStreamInfo Audio(int index, string language, string title = "") =>
        ProbeStreamInfo.Parse(
            $$$"""{"index": {{{index}}}, "codec_type": "audio", "codec_name": "aac", "channels": 2, "tags": {"language": "{{{language}}}", "title": "{{{title}}}"}}""");

    private static ProcessingRulesConfig EnglishOnly() => RemuxRules.DefaultConfig() with
    {
        AudioPreferenceMode = RemuxRuleValues.PolicyPreferredLangsStrict,
        PrimaryAudioLang = "eng",
        SecondaryAudioLang = string.Empty,
        TertiaryAudioLang = string.Empty,
    };

    private static string Explain(ProcessingRulesConfig config, string? profile, params ProbeStreamInfo[] audio)
    {
        Assert.Null(RemuxRules.PlanRemux([Video], audio, [], config));
        return AudioRejectionExplanation.Explain(config, audio, profile).Sentence;
    }

    [Fact]
    public void A_file_with_only_foreign_audio_names_the_language_the_rules_keep_and_the_one_it_has()
    {
        var reason = Explain(EnglishOnly(), "Movies", Audio(1, "jpn"));

        Assert.Equal(
            "Rejected: none of its audio tracks are in English, and the \"Movies\" rules keep only English audio, so there would be nothing to keep. " +
            "It has Japanese audio. To accept files like this, change the first-choice language or \"How to choose audio\" in Settings › Rules.",
            reason);
    }

    [Fact]
    public void Several_foreign_languages_are_listed_once_each()
    {
        var reason = Explain(EnglishOnly(), null, Audio(1, "jpn"), Audio(2, "fre"), Audio(3, "jpn"));

        Assert.Contains("your rules keep only English audio", reason, StringComparison.Ordinal);
        Assert.Contains("It has Japanese and French audio.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Tracks_with_no_language_say_so_instead_of_naming_one()
    {
        var reason = Explain(EnglishOnly(), null, Audio(1, "und"));

        Assert.Contains("None of its audio tracks say which language they are in.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Preferred_languages_only_with_no_first_choice_names_the_missing_setting()
    {
        var config = EnglishOnly() with { PrimaryAudioLang = string.Empty };

        var reason = Explain(config, "TV", Audio(1, "eng"));

        Assert.Equal(
            "Rejected: the \"TV\" rules keep only your preferred audio languages, but no first-choice language is set, so no track can be kept. " +
            "Choose a first-choice language, or change \"How to choose audio\", in Settings › Rules.",
            reason);
    }

    [Fact]
    public void A_file_of_only_commentary_names_the_remove_commentary_rule()
    {
        var config = RemuxRules.DefaultConfig() with { RemoveCommentary = true };

        var reason = Explain(config, null, Audio(1, "eng", "Director commentary"));

        Assert.Equal(
            "Rejected: every audio track in this file is commentary, and your rules remove commentary, so no audio would be left. " +
            "Turn off \"Remove commentary tracks\" in Settings › Rules to keep files like this.",
            reason);
    }

    [Fact]
    public void A_file_with_no_audio_says_there_is_nothing_to_keep()
    {
        var reason = Explain(RemuxRules.DefaultConfig(), null);

        Assert.Equal("Rejected: this file has no audio tracks, so there would be nothing to keep.", reason);
    }

    [Fact]
    public void The_summary_for_a_title_names_the_language_and_the_rules()
    {
        var audio = new[] { Audio(1, "jpn") };

        var rejection = AudioRejectionExplanation.Explain(EnglishOnly(), audio, "Movies");

        Assert.Equal("no English audio for the \"Movies\" rules", rejection.Summary);
    }
}
