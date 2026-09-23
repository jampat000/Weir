using Weir.Core.Processing;
using Weir.Core.Rules;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// Issue #545 item 4: <see cref="RemuxPassPaths.RulesConfigFor"/> (the path a live remux job takes) and the fallback
/// path (<see cref="RuleSetConversion.ToRulesConfig"/>) must normalize the stored subtitle mode the same way.
/// <c>keep_all</c> — the stored default, and "Keep all subtitles" on the Rules screen — keeps every subtitle rather than
/// normalizing to keep-selected (which removes every subtitle when no language is listed), and an unknown value errs
/// towards keeping rather than removing.
/// </summary>
public sealed class SubtitleModeNormalizationTests
{
    private static ProcessingRuleSetRecord RuleSet(string subtitleMode) => new() { Name = "Test", SubtitleMode = subtitleMode };

    [Theory]
    [InlineData("keep_all", RemuxRuleValues.SubtitleModeKeepAll)]
    [InlineData("KEEP_ALL", RemuxRuleValues.SubtitleModeKeepAll)]
    [InlineData("", RemuxRuleValues.SubtitleModeKeepAll)]
    [InlineData("bogus", RemuxRuleValues.SubtitleModeKeepAll)]
    [InlineData("keep_listed", RemuxRuleValues.SubtitleModeKeepSelected)]
    [InlineData("keep_selected", RemuxRuleValues.SubtitleModeKeepSelected)]
    [InlineData("remove_all", RemuxRuleValues.SubtitleModeRemoveAll)]
    [InlineData("REMOVE_ALL", RemuxRuleValues.SubtitleModeRemoveAll)]
    [InlineData(" remove_all ", RemuxRuleValues.SubtitleModeRemoveAll)]
    public void Both_config_paths_normalize_the_stored_mode_the_same_way(string stored, string expected)
    {
        var ruleSet = RuleSet(stored);

        var live = RemuxPassPaths.RulesConfigFor(ruleSet);
        var fallback = RuleSetConversion.ToRulesConfig(ruleSet);

        Assert.Equal(expected, live.SubtitleMode);
        Assert.Equal(expected, fallback.SubtitleMode);
        Assert.Equal(fallback.SubtitleMode, live.SubtitleMode);
    }
}
