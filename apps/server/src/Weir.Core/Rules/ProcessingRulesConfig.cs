namespace Weir.Core.Rules;

/// <summary>
/// The rules in force for one pass.
/// <see cref="SubtitleMode"/> and <see cref="AudioPreferenceMode"/> stay strings: a subtitle mode is one of
/// <c>remove_all</c>, <c>keep_selected</c> or <c>keep_all</c> once <c>RuleSetConversion.NormalizeSubtitleMode</c>
/// has read it, and the planner normalizes an unknown policy to the default.
/// </summary>
public sealed record ProcessingRulesConfig
{
    /// <summary>
    /// Whether these rules drop every subtitle track: remove-all, or keep-selected with no language chosen.
    /// Keep-all never does. The planner, the per-track explanation and the removed-track comparison all ask
    /// this one question, so they cannot disagree about which files lose their subtitles.
    /// </summary>
    public bool RemovesEverySubtitle =>
        SubtitleMode == RemuxRuleValues.SubtitleModeRemoveAll
        || (SubtitleMode != RemuxRuleValues.SubtitleModeKeepAll && SubtitleLangs.Count == 0);

    /// <summary>Whether these rules keep every subtitle track whatever its language (subject to the hearing-impaired rule and the per-language cap).</summary>
    public bool KeepsEverySubtitleLanguage => SubtitleMode == RemuxRuleValues.SubtitleModeKeepAll;

    public required string PrimaryAudioLang { get; init; }
    public required string SecondaryAudioLang { get; init; }
    public required string TertiaryAudioLang { get; init; }
    public required string DefaultAudioSlot { get; init; }
    public required bool RemoveCommentary { get; init; }
    public required string SubtitleMode { get; init; }

    /// <summary>
    /// Issue #537 item 3: normalized the same way a track's own language tag is (<see cref="RemuxRules.NormalizeLang"/>),
    /// so "ENG" or "en-US" matches a file tagged "eng". Issue #496: a recognized variant identifier ("fre-CA", or "fr-CA" as a BCP 47 tag) is kept
    /// instead of being reduced to its base language, so a list can single out a regional dub.
    /// </summary>
    public required IReadOnlyList<string> SubtitleLangs { get; init; }

    public required bool PreserveForcedSubs { get; init; }
    public required bool PreserveDefaultSubs { get; init; }
    public required string AudioPreferenceMode { get; init; }

    /// <summary>The ordered sorter list, as stored JSON. Empty means the seeded default.</summary>
    public string AudioSortersJson { get; init; } = string.Empty;

    /// <summary>
    /// The profile's "Subtitle order": how the kept subtitle tracks are ranked. Empty (no order saved) keeps the default
    /// order: the file's own for keep-all, the languages-to-keep list for keep-selected.
    /// </summary>
    public string SubtitleSortersJson { get; init; } = string.Empty;

    /// <summary>
    /// Issue #495: remove a subtitle track whose <see cref="TrackFlags.HearingImpaired"/> flag is
    /// set (SDH/CC), from its disposition or its name. Off by default, so an upgrade changes nothing.
    /// </summary>
    public bool RemoveHearingImpairedSubs { get; init; }

    /// <summary>
    /// Issue #497: <see cref="RemuxRuleValues.AudioKeepModeSingle"/> (the default, which every golden file
    /// assumes) or <see cref="RemuxRuleValues.AudioKeepModePerLanguage"/>, which keeps the best track of each
    /// configured language slot (primary/secondary/tertiary) that has one, instead of a single winner.
    /// </summary>
    public string AudioKeepMode { get; init; } = RemuxRuleValues.AudioKeepModeSingle;

    /// <summary>
    /// Issue #497: caps how many subtitle tracks survive per language, keeping the best by
    /// <see cref="SubtitleQualityStrategy"/>. 0 (the default) means unlimited.
    /// A forced track kept under <see cref="PreserveForcedSubs"/> does not count toward the cap.
    /// </summary>
    public int SubtitleMaxPerLanguage { get; init; }

    /// <summary>
    /// Issue #497: how the subtitle cap picks a winner within a language —
    /// <see cref="RemuxRuleValues.SubtitleStrategyTextFirst"/> (default), <c>image_first</c> or
    /// <c>accessibility</c>. Unused while <see cref="SubtitleMaxPerLanguage"/> is 0.
    /// </summary>
    public string SubtitleQualityStrategy { get; init; } = RemuxRuleValues.SubtitleStrategyTextFirst;

    /// <summary>Metadata and attachment stripping. All off by default.</summary>
    public MetadataRules Metadata { get; init; } = new();

    /// <summary>Carried for the pass; the planner itself reads only the resolved hint below.</summary>
    public OriginalLanguageRules? OriginalLanguage { get; init; }

    /// <summary>Input indices the pass wants preferred, in order, from the metadata lookup.</summary>
    public IReadOnlyList<int> PreferredAudioIndices { get; init; } = [];

    /// <summary>The sentence explaining which mechanism chose, appended to the selection notes.</summary>
    public string OriginalLanguageNote { get; init; } = string.Empty;

    /// <summary>
    /// Issue #537 item 4: feeds an original-language decision (from the metadata lookup, #520) in
    /// cleanly. <see cref="PreferredAudioIndices"/> and <see cref="OriginalLanguageNote"/> flow straight
    /// into <see cref="RemuxRules.PlanRemux"/> unchanged; this saves a caller copying both fields by hand.
    /// </summary>
    public ProcessingRulesConfig WithOriginalLanguage(OriginalLanguageOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return this with { PreferredAudioIndices = outcome.PreferredIndices, OriginalLanguageNote = outcome.Note };
    }
}
