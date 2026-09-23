import { Field } from "../../../../components/shared/field";
import { LanguageMultiField, LanguageSelectField } from "./language-fields";
import {
  RuleGroup,
  RuleSelect,
  RuleToggle,
  type RuleSetBinding,
} from "./rule-set-fields";
import { defaultAudioSortersFor, dumpSorters } from "./rule-set-model";

const AUDIO_STRATEGIES = [
  {
    value: "preferred_langs_quality",
    label: "Preferred languages, then quality",
  },
  { value: "preferred_langs_strict", label: "Preferred languages only" },
  { value: "quality_all_languages", label: "Best quality in any language" },
];

const DEFAULT_SLOTS = [
  { value: "primary", label: "First choice" },
  { value: "secondary", label: "Second choice" },
  { value: "tertiary", label: "Third choice" },
];

const AUDIO_KEEP_MODES = [
  { value: "single", label: "One winning track (today's behavior)" },
  { value: "per_language", label: "Best track of each configured language" },
];

const SUBTITLE_MODES = [
  { value: "keep_all", label: "Keep all subtitles" },
  { value: "keep_listed", label: "Keep selected languages" },
  { value: "remove_all", label: "Remove all subtitles" },
];

const SUBTITLE_STRATEGIES = [
  { value: "text_first", label: "Text subtitles first (SRT/ASS over PGS)" },
  { value: "image_first", label: "Image subtitles first (PGS over SRT/ASS)" },
  { value: "accessibility", label: "Hearing-impaired (SDH) first" },
];

/** Step 1: the language priority, and which kept track becomes the default. */
export function AudioRulesGroup({ binding }: { binding: RuleSetBinding }) {
  const { draft, change, disabled } = binding;
  return (
    <RuleGroup
      step={1}
      title="Audio"
      detail="Choose the language priority and which retained track becomes default."
    >
      <RuleSelect
        binding={binding}
        name="audio_preference_mode"
        label="Selection strategy"
        options={AUDIO_STRATEGIES}
        // A new strategy starts from its own default order.
        onChange={(mode) => {
          change("audio_preference_mode", mode);
          change(
            "audio_sorters_json",
            dumpSorters(defaultAudioSortersFor(mode)),
          );
        }}
      />
      <div className="mm-field-row">
        <LanguageSelectField
          label="First choice"
          value={draft.primary_audio_lang}
          disabled={disabled}
          onChange={(value) => change("primary_audio_lang", value)}
        />
        <LanguageSelectField
          label="Second choice"
          value={draft.secondary_audio_lang}
          noneLabel="None"
          disabled={disabled}
          onChange={(value) => change("secondary_audio_lang", value)}
        />
        <LanguageSelectField
          label="Third choice"
          value={draft.tertiary_audio_lang}
          noneLabel="None"
          disabled={disabled}
          onChange={(value) => change("tertiary_audio_lang", value)}
        />
        <RuleSelect
          binding={binding}
          name="default_audio_slot"
          label="Mark as default"
          options={DEFAULT_SLOTS}
        />
      </div>
      <RuleToggle
        binding={binding}
        name="remove_commentary"
        label="Remove commentary tracks"
        detail="Exclude commentary before selecting the preferred audio."
      />
      <RuleSelect
        binding={binding}
        name="audio_keep_mode"
        label="Audio tracks kept"
        options={AUDIO_KEEP_MODES}
        hint={`"Best track of each configured language" keeps the original alongside a dub, e.g. Japanese plus an English dub, each the best available track of its language. Never keeps zero audio tracks.`}
      />
    </RuleGroup>
  );
}

/** Only while the per-language cap is set does it matter which subtitle counts as best. */
function SubtitleCapFields({ binding }: { binding: RuleSetBinding }) {
  const { draft, change, disabled } = binding;
  return (
    <div className="mm-field-row">
      <Field
        label="Limit subtitles kept per language"
        width="short"
        hint="0 means unlimited (today's behavior). A forced track kept above doesn't count toward this cap."
      >
        <input
          type="number"
          min={0}
          className="mm-input"
          value={draft.subtitle_max_per_language}
          disabled={disabled}
          onChange={(event) =>
            change(
              "subtitle_max_per_language",
              Math.max(0, Number(event.target.value) || 0),
            )
          }
        />
      </Field>
      <RuleSelect
        binding={binding}
        name="subtitle_quality_strategy"
        label="How to pick the best subtitle"
        options={SUBTITLE_STRATEGIES}
        hint="Only used while the cap above is set."
        disabled={disabled || draft.subtitle_max_per_language === 0}
      />
    </div>
  );
}

/** Step 2: which subtitles are kept, and which keep their flags. */
export function SubtitleRulesGroup({ binding }: { binding: RuleSetBinding }) {
  const { draft, change, disabled } = binding;
  return (
    <RuleGroup
      step={2}
      title="Subtitles"
      detail="Choose what is retained and which tracks keep their flags."
    >
      <RuleSelect
        binding={binding}
        name="subtitle_mode"
        label="Subtitle handling"
        options={SUBTITLE_MODES}
      />
      {draft.subtitle_mode === "keep_listed" ? (
        <LanguageMultiField
          label="Languages to keep"
          value={draft.subtitle_langs_csv}
          disabled={disabled}
          onChange={(value) => change("subtitle_langs_csv", value)}
        />
      ) : null}
      {draft.subtitle_mode !== "remove_all" ? (
        <div className="grid gap-3">
          <RuleToggle
            binding={binding}
            name="preserve_forced_subs"
            label="Keep forced subtitles"
            detail="Preserve tracks needed to translate foreign dialogue. A signs track counts as forced too."
          />
          <RuleToggle
            binding={binding}
            name="preserve_default_subs"
            label="Keep default subtitles"
            detail="Preserve tracks already marked as default."
          />
          <RuleToggle
            binding={binding}
            name="remove_hearing_impaired_subs"
            label="Remove hearing-impaired subtitles"
            detail='Drop a subtitle track detected as SDH/CC, from its flag or its name (e.g. "English (SDH)").'
          />
          <SubtitleCapFields binding={binding} />
        </div>
      ) : null}
    </RuleGroup>
  );
}
