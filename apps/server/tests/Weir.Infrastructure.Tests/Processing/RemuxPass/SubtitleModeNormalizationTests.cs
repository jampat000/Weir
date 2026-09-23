using Weir.Core.Processing;
using Weir.Core.Rules;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// Issue #545 item 4: <c>rules_config_for</c> (the path a live remux job takes) used to pass the stored subtitle mode
/// through unchanged, while the fallback path (<see cref="RuleSetConversion.ToRulesConfig"/>) normalized it, so both must
/// agree. The mapping itself changed later: <c>keep_all</c> — the stored default, and "Keep all subtitles" on the Rules
/// screen — used to normalize to keep-selected and so removed every subtitle when no language was listed. It now keeps
/// every subtitle, and an unknown value errs towards keeping rather than removing.
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
