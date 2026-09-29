namespace Weir.Core.Rules;

/// <summary>
/// Says, in plain words, why the audio rules left a file with nothing to keep, and which setting to change.
/// It follows the same cases <see cref="RemuxRules.PlanRemux"/> returns no plan for, so the sentence names the rule that
/// actually decided.
/// </summary>
public static class AudioRejectionExplanation
{
    private const string AudioChoiceSetting = "\"How to choose audio\"";
    private const string RulesScreen = "Settings › Rules";
    private const string UndeterminedLanguage = "und";

    /// <summary>
    /// The sentence a person reads for a file the audio rules rejected. <paramref name="profileName"/> is the rules profile
    /// the file was checked against, when it has one.
    /// </summary>
    public static string Explain(ProcessingRulesConfig config, IReadOnlyList<ProbeStreamInfo> audio, string? profileName)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(audio);
        var rules = RulesLabel(profileName);
        if (audio.Count == 0)
        {
            return "Rejected: this file has no audio tracks, so there would be nothing to keep.";
        }

        var kept = audio.Where(stream => !(config.RemoveCommentary && TrackFlagsReader.Detect(stream).Commentary.Value)).ToList();
        if (kept.Count == 0)
        {
            return $"Rejected: every audio track in this file is commentary, and {rules} remove commentary, so no audio would be left. " +
                   $"Turn off \"Remove commentary tracks\" in {RulesScreen} to keep files like this.";
        }

        if (RemuxRules.NormalizeAudioPreferenceMode(config.AudioPreferenceMode) != RemuxRuleValues.PolicyPreferredLangsStrict)
        {
            return $"Rejected: {rules} leave no audio track to keep. Check the audio settings in {RulesScreen}.";
        }

        var wanted = RemuxRules.OrderedPreferenceLangs(config);
        if (wanted.Count == 0)
        {
            return $"Rejected: {rules} keep only your preferred audio languages, but no first-choice language is set, so no track can be kept. " +
                   $"Choose a first-choice language, or change {AudioChoiceSetting}, in {RulesScreen}.";
        }

        var language = LanguageName(wanted[0]);
        return $"Rejected: none of its audio tracks are in {language}, and {rules} keep only {language} audio, so there would be nothing to keep. " +
               $"{FoundSentence(kept)} To accept files like this, change the first-choice language or {AudioChoiceSetting} in {RulesScreen}.";
    }

    private static string RulesLabel(string? profileName) =>
        string.IsNullOrWhiteSpace(profileName) ? "your rules" : $"the \"{profileName.Trim()}\" rules";

    private static string LanguageName(string configured) =>
        LanguageVariants.IsVariantIdentifier(configured) ? LanguageVariants.DisplayName(configured) : RemuxDisplay.LangDisplay(configured);

    /// <summary>The languages the file does have, or that its tracks name none.</summary>
    private static string FoundSentence(IReadOnlyList<ProbeStreamInfo> audio)
    {
        var languages = audio
            .Select(stream => RemuxRules.NormalizeLang(stream.Tag("language")))
            .Where(code => code.Length > 0 && code != UndeterminedLanguage)
            .Select(RemuxDisplay.LangDisplay)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return languages.Count == 0
            ? "None of its audio tracks say which language they are in."
            : $"It has {JoinNames(languages)} audio.";
    }

    private static string JoinNames(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
