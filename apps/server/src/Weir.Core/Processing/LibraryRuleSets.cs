using Weir.Core.Rules;

namespace Weir.Core.Processing;

/// <summary>Rule-set field validation, split out of <see cref="LibraryRules"/>, which owns library field validation.</summary>
public static partial class LibraryRules
{
    /// <summary>The fields a rule-set create/update request carries.</summary>
    public sealed record RuleSetInput
    {
        public required string Name { get; init; }
        public string PrimaryAudioLang { get; init; } = string.Empty;
        public string SecondaryAudioLang { get; init; } = string.Empty;
        public string TertiaryAudioLang { get; init; } = string.Empty;
        public string DefaultAudioSlot { get; init; } = "primary";
        public bool RemoveCommentary { get; init; }
        public string SubtitleMode { get; init; } = "keep_all";
        public string SubtitleLangsCsv { get; init; } = string.Empty;
        public bool PreserveForcedSubs { get; init; } = true;
        public bool PreserveDefaultSubs { get; init; } = true;
        public string AudioSortersJson { get; init; } = string.Empty;
        public string SubtitleSortersJson { get; init; } = string.Empty;
        public bool KeepOriginalLanguage { get; init; }
        public string OriginalLanguageAdditionalCsv { get; init; } = string.Empty;
        public bool OriginalLanguageKeepOnlyFirst { get; init; } = true;
        public bool OriginalLanguageFirstIfNone { get; init; } = true;
        public bool OriginalLanguageTreatEmptyAsOriginal { get; init; }
        public bool RemoveImages { get; init; }
        public bool RemoveAttachments { get; init; }
        public bool RemoveTitle { get; init; }
        public bool RemoveLanguageTags { get; init; }
        public bool RemoveOtherMetadata { get; init; }
        public string AudioPreferenceMode { get; init; } = "preferred_langs_quality";

        /// <summary>Issue #495.</summary>
        public bool RemoveHearingImpairedSubs { get; init; }

        /// <summary>Issue #497.</summary>
        public string AudioKeepMode { get; init; } = RemuxRuleValues.AudioKeepModeSingle;

        /// <summary>Issue #497.</summary>
        public int SubtitleMaxPerLanguage { get; init; }

        /// <summary>Issue #497.</summary>
        public string SubtitleQualityStrategy { get; init; } = RemuxRuleValues.SubtitleStrategyTextFirst;

        /// <summary>Issue #498.</summary>
        public bool StandardizeTrackNames { get; init; }

        /// <summary>Issue #498.</summary>
        public string TrackNameTemplate { get; init; } = TrackNaming.DefaultTemplate;

        /// <summary>Issue #498.</summary>
        public TrackNameOverrides TrackNameOverrides { get; init; } = new();

        /// <summary>Issue #498.</summary>
        public bool ClearVideoTrackNames { get; init; }

        /// <summary>Issue #498.</summary>
        public bool RemoveChapters { get; init; }
    }

    /// <summary>Applies validated rule-set fields, including the audio and subtitle sorters, onto a rule-set record.</summary>
    public static ProcessingRuleSetRecord ApplyRuleSetFields(ProcessingRuleSetRecord row, RuleSetInput body)
    {
        string audioSorters;
        try
        {
            audioSorters = TrackSorters.Validate(body.AudioSortersJson);
        }
        catch (TrackSorterException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        string subtitleSorters;
        try
        {
            subtitleSorters = TrackSorters.Validate(body.SubtitleSortersJson);
        }
        catch (TrackSorterException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        // AudioKeepMode and SubtitleQualityStrategy are closed enumerations validated at the HTTP boundary
        // (BodyModel.Literal, like DefaultAudioSlot/SubtitleMode/AudioPreferenceMode); SubtitleMaxPerLanguage's
        // "no negative" rule is validated the same way (BodyModel.Number's ge: 0). Nothing further to check here.
        var metadataForValidation = new MetadataRules
        {
            TrackNameTemplate = body.TrackNameTemplate,
            TrackNameOverrides = body.TrackNameOverrides,
        };
        try
        {
            TrackNaming.ValidateAll(metadataForValidation);
        }
        catch (TrackNameTemplateException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        // No preset fallback for an empty audio sorter list is needed: TrackSorters.Validate never
        // returns an empty string, because empty input already becomes the default sorter list.
        return row with
        {
            PrimaryAudioLang = body.PrimaryAudioLang,
            SecondaryAudioLang = body.SecondaryAudioLang,
            TertiaryAudioLang = body.TertiaryAudioLang,
            DefaultAudioSlot = body.DefaultAudioSlot,
            RemoveCommentary = body.RemoveCommentary,
            SubtitleMode = body.SubtitleMode,
            SubtitleLangsCsv = body.SubtitleLangsCsv,
            PreserveForcedSubs = body.PreserveForcedSubs,
            PreserveDefaultSubs = body.PreserveDefaultSubs,
            AudioPreferenceMode = body.AudioPreferenceMode,
            KeepOriginalLanguage = body.KeepOriginalLanguage,
            OriginalLanguageAdditionalCsv = body.OriginalLanguageAdditionalCsv,
            OriginalLanguageKeepOnlyFirst = body.OriginalLanguageKeepOnlyFirst,
            OriginalLanguageFirstIfNone = body.OriginalLanguageFirstIfNone,
            OriginalLanguageTreatEmptyAsOriginal = body.OriginalLanguageTreatEmptyAsOriginal,
            RemoveImages = body.RemoveImages,
            RemoveAttachments = body.RemoveAttachments,
            RemoveTitle = body.RemoveTitle,
            RemoveLanguageTags = body.RemoveLanguageTags,
            RemoveOtherMetadata = body.RemoveOtherMetadata,
            AudioSortersJson = audioSorters,
            SubtitleSortersJson = subtitleSorters,
            RemoveHearingImpairedSubs = body.RemoveHearingImpairedSubs,
            AudioKeepMode = body.AudioKeepMode,
            SubtitleMaxPerLanguage = body.SubtitleMaxPerLanguage,
            SubtitleQualityStrategy = body.SubtitleQualityStrategy,
            StandardizeTrackNames = body.StandardizeTrackNames,
            TrackNameTemplate = body.TrackNameTemplate,
            TrackNameOverrides = body.TrackNameOverrides,
            ClearVideoTrackNames = body.ClearVideoTrackNames,
            RemoveChapters = body.RemoveChapters,
        };
    }
}
