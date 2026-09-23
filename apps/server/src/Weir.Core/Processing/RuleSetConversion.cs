using Weir.Core.Rules;

namespace Weir.Core.Processing;

/// <summary>
/// Convert a stored rule set into the config the remux planner takes, with the rule-less fallback.
/// </summary>
public static class RuleSetConversion
{
    /// <summary>
    /// A stored subtitle mode as the planner reads it. <c>remove_all</c> removes every subtitle;
    /// <c>keep_listed</c> (what Settings › Rules saves) and <c>keep_selected</c> keep the listed languages;
    /// anything else, including the stored default <c>keep_all</c>, keeps every subtitle. Treating keep-all as
    /// keep-selected would remove every track, because its language list is hidden and empty; so an unknown value
    /// errs towards keeping too.
    /// </summary>
    public static string NormalizeSubtitleMode(string? raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            RemuxRuleValues.SubtitleModeRemoveAll => RemuxRuleValues.SubtitleModeRemoveAll,
            "keep_listed" or RemuxRuleValues.SubtitleModeKeepSelected => RemuxRuleValues.SubtitleModeKeepSelected,
            _ => RemuxRuleValues.SubtitleModeKeepAll,
        };

    /// <summary>
    /// A profile holding exactly the rules Weir applies to a library with none (<see cref="RemuxRules.DefaultConfig"/>),
    /// so giving such a library a profile changes nothing about what happens to its files. Every library has a profile.
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

    /// <summary>One rule set as the config the planner takes. A missing rule set yields the shipped defaults.</summary>
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
