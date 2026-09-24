using Weir.Core.Json;

namespace Weir.Core.Rules;

/// <summary>Canonicalizes the policy-name strings stored in <see cref="ProcessingRulesConfig"/>, falling back to each policy's default for an unrecognized stored value.</summary>
public static partial class RemuxRules
{
    /// <summary>The canonical policy; unknown stored values use the default policy.</summary>
    public static string NormalizeAudioPreferenceMode(string? raw)
    {
        var m = RulesJson.Lower(WireStrings.Strip(raw ?? string.Empty));
        return m is RemuxRuleValues.PolicyPreferredLangsQuality or RemuxRuleValues.PolicyPreferredLangsStrict or RemuxRuleValues.PolicyQualityAllLanguages
            ? m
            : RemuxRuleValues.PolicyPreferredLangsQuality;
    }

    /// <summary>Issue #497: the canonical audio keep mode; unknown stored values use "single".</summary>
    public static string NormalizeAudioKeepMode(string? raw)
    {
        var m = RulesJson.Lower(WireStrings.Strip(raw ?? string.Empty));
        return m == RemuxRuleValues.AudioKeepModePerLanguage ? RemuxRuleValues.AudioKeepModePerLanguage : RemuxRuleValues.AudioKeepModeSingle;
    }

    /// <summary>Issue #497: the canonical subtitle quality strategy; unknown stored values use "text_first".</summary>
    public static string NormalizeSubtitleQualityStrategy(string? raw)
    {
        var m = RulesJson.Lower(WireStrings.Strip(raw ?? string.Empty));
        return m is RemuxRuleValues.SubtitleStrategyImageFirst or RemuxRuleValues.SubtitleStrategyAccessibility
            ? m
            : RemuxRuleValues.SubtitleStrategyTextFirst;
    }
}
