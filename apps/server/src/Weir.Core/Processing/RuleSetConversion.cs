using Weir.Core.Rules;

namespace Weir.Core.Processing;

/// <summary>
/// Convert a stored rule set into the config the remux planner takes (port of
/// <c>processing_remux_rules_settings_service.rule_set_to_rules_config</c>). Deliberately the simpler of the
/// two Python conversions: the ADR-0014 module docstring notes its HTTP view was removed in #460 and only
/// the conversion itself, plus the rule-less fallback, remain.
/// </summary>
public static class RuleSetConversion
{
    /// <summary>
    /// A stored subtitle mode as the planner reads it. <c>remove_all</c> removes every subtitle;
    /// <c>keep_listed</c> (what Settings › Rules saves) and <c>keep_selected</c> keep the listed languages;
    /// anything else, including the stored default <c>keep_all</c>, keeps every subtitle. This used to map
    /// everything but <c>remove_all</c> to keep-selected, so "Keep all subtitles" — with its language list
    /// hidden and empty — removed every subtitle track. An unknown value now errs towards keeping.
    /// </summary>
    public static string NormalizeSubtitleMode(string? raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            RemuxRuleValues.SubtitleModeRemoveAll => RemuxRuleValues.SubtitleModeRemoveAll,
            "keep_listed" or RemuxRuleValues.SubtitleModeKeepSelected => RemuxRuleValues.SubtitleModeKeepSelected,
            _ => RemuxRuleValues.SubtitleModeKeepAll,
        };

    /// <summary>One rule set as the config the planner takes. A missing rule set yields the shipped defaults.</summary>
    /// <summary>
    /// A profile holding exactly the rules Weir used for a library with none (<see cref="RemuxRules.DefaultConfig"/>), so
    /// giving such a library a profile changes nothing about what happens to its files (James, 23 Sep 2026: every library
    /// has a profile, made from today's behaviour).
    /// </summary>
    public static ProcessingRuleSetRecord BuiltInDefaults(string name)
    {
        var rules = RemuxRules.DefaultConfig();
        return new ProcessingRuleSetRecord
        {
            Name = name,
            PrimaryAudioLang = rules.PrimaryAudioLang,
            SecondaryAudioLang = rules.SecondaryAudioLang,
            TertiaryAudioLang = rules.TertiaryAudioLang,
            DefaultAudioSlot = rules.DefaultAudioSlot,
            RemoveCommentary = rules.RemoveCommentary,
            SubtitleMode = rules.SubtitleMode,
            SubtitleLangsCsv = string.Join(",", rules.SubtitleLangs),
            PreserveForcedSubs = rules.PreserveForcedSubs,
            PreserveDefaultSubs = rules.PreserveDefaultSubs,
            AudioPreferenceMode = rules.AudioPreferenceMode,
            AudioSortersJson = rules.AudioSortersJson,
            RemoveHearingImpairedSubs = rules.RemoveHearingImpairedSubs,
            AudioKeepMode = rules.AudioKeepMode,
            SubtitleMaxPerLanguage = rules.SubtitleMaxPerLanguage,
            SubtitleQualityStrategy = rules.SubtitleQualityStrategy,
        };
    }

    public static ProcessingRulesConfig ToRulesConfig(ProcessingRuleSetRecord? row)
    {
        if (row is null)
        {
            return RemuxRules.DefaultConfig();
        }

        return new ProcessingRulesConfig
        {
            PrimaryAudioLang = row.PrimaryAudioLang,
            SecondaryAudioLang = row.SecondaryAudioLang,
            TertiaryAudioLang = row.TertiaryAudioLang,
            DefaultAudioSlot = row.DefaultAudioSlot,
            RemoveCommentary = row.RemoveCommentary,
            SubtitleMode = NormalizeSubtitleMode(row.SubtitleMode),
            SubtitleLangs = [.. (row.SubtitleLangsCsv ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)],
            PreserveForcedSubs = row.PreserveForcedSubs,
            PreserveDefaultSubs = row.PreserveDefaultSubs,
            AudioPreferenceMode = RemuxRules.NormalizeAudioPreferenceMode(row.AudioPreferenceMode),
            AudioSortersJson = row.AudioSortersJson ?? string.Empty,
            SubtitleSortersJson = row.SubtitleSortersJson ?? string.Empty,
            RemoveHearingImpairedSubs = row.RemoveHearingImpairedSubs,
            AudioKeepMode = RemuxRules.NormalizeAudioKeepMode(row.AudioKeepMode),
            SubtitleMaxPerLanguage = row.SubtitleMaxPerLanguage,
            SubtitleQualityStrategy = RemuxRules.NormalizeSubtitleQualityStrategy(row.SubtitleQualityStrategy),
            Metadata = new MetadataRules
            {
                RemoveImages = row.RemoveImages,
                RemoveAttachments = row.RemoveAttachments,
                RemoveTitle = row.RemoveTitle,
                RemoveLanguageTags = row.RemoveLanguageTags,
                RemoveOtherMetadata = row.RemoveOtherMetadata,
                StandardizeTrackNames = row.StandardizeTrackNames,
                TrackNameTemplate = row.TrackNameTemplate,
                TrackNameOverrides = row.TrackNameOverrides,
                ClearVideoTrackNames = row.ClearVideoTrackNames,
                RemoveChapters = row.RemoveChapters,
            },
        };
    }
}
