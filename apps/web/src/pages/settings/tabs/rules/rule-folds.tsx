import type { ProcessingTrackNameOverrides } from "../../../../lib/processing/rule-sets-api";
import type { TrackNamePreviewFlags } from "../../../../lib/processing/track-name-preview";
import { LanguageMultiField } from "./language-fields";
import {
  RuleFold,
  RuleToggle,
  TrackNameTemplateField,
  type RuleSetBinding,
} from "./rule-set-fields";

function countOn(values: boolean[]): number {
  return values.filter(Boolean).length;
}

/** Keep the title's original spoken language as well; it needs a metadata provider to know it. */
export function OriginalLanguageFold({
  binding,
  providerName,
}: {
  binding: RuleSetBinding;
  providerName: string;
}) {
  const { draft, change, disabled } = binding;
  return (
    <RuleFold
      title="Original language"
      detail="Optionally keep the title's original spoken language as well."
      on={draft.keep_original_language ? 1 : 0}
      of={1}
    >
      <RuleToggle
        binding={binding}
        name="keep_original_language"
        label="Keep the original language"
        detail={
          providerName
            ? `Uses ${providerName.toUpperCase()} when it can identify the title.`
            : "Requires the metadata provider configured below."
        }
      />
      {draft.keep_original_language ? (
        <div className="mm-rule-indent space-y-3">
          <LanguageMultiField
            label="Other languages to keep"
            value={draft.original_language_additional_csv}
            disabled={disabled}
            onChange={(value) =>
              change("original_language_additional_csv", value)
            }
          />
          <RuleToggle
            binding={binding}
            name="original_language_keep_only_first"
            label="Keep one track per language"
            detail="Avoid duplicate tracks in the same language."
          />
          <RuleToggle
            binding={binding}
            name="original_language_first_if_none"
            label="Use audio preferences if no match is found"
            detail="Keeps the profile safe when metadata is incomplete."
          />
          <RuleToggle
            binding={binding}
            name="original_language_treat_empty_as_original"
            label="Treat an untagged track as original"
            detail="Useful when the main track has no language tag."
          />
        </div>
      ) : null}
    </RuleFold>
  );
}

const CONTAINER_RULES = [
  {
    name: "remove_images",
    label: "Embedded images",
    detail: "Strip embedded cover art.",
  },
  {
    name: "remove_attachments",
    label: "Attachments",
    detail: "Strip fonts and other attached files.",
  },
  {
    name: "remove_title",
    label: "The file's built-in title",
    detail: "Remove the container title only.",
  },
  {
    name: "remove_language_tags",
    label: "Language tags",
    detail: "Strip language metadata after selection.",
  },
  {
    name: "remove_other_metadata",
    label: "Other metadata",
    detail: "Strip other container-level tags.",
  },
] as const;

/** Streams and tags Weir strips once it has chosen the tracks. */
export function ContainerFold({ binding }: { binding: RuleSetBinding }) {
  return (
    <RuleFold
      title="Also remove"
      detail="Optional streams and tags Weir strips after it has chosen the tracks."
      on={countOn(CONTAINER_RULES.map((rule) => binding.draft[rule.name]))}
      of={CONTAINER_RULES.length}
    >
      <div className="grid gap-3">
        {CONTAINER_RULES.map((rule) => (
          <RuleToggle key={rule.name} binding={binding} {...rule} />
        ))}
      </div>
    </RuleFold>
  );
}

const OVERRIDES: {
  key: keyof ProcessingTrackNameOverrides;
  label: string;
  flags: TrackNamePreviewFlags;
}[] = [
  { key: "forced", label: "Forced tracks", flags: { forced: true } },
  {
    key: "hearing_impaired",
    label: "Hearing-impaired tracks",
    flags: { hearingImpaired: true },
  },
  {
    key: "commentary",
    label: "Commentary tracks",
    flags: { commentary: true },
  },
  {
    key: "audio_description",
    label: "Audio description tracks",
    flags: { audioDescription: true },
  },
];

function TrackNameTemplates({ binding }: { binding: RuleSetBinding }) {
  const { draft, change, disabled } = binding;
  return (
    <div className="mm-rule-indent space-y-4">
      <TrackNameTemplateField
        label="Track name template"
        detail="Placeholders: {language} {variant} {channels} {codec} {flags}."
        value={draft.track_name_template}
        disabled={disabled}
        onChange={(value) => change("track_name_template", value)}
      />
      <div className="space-y-3">
        <p className="text-xs font-medium text-mm-text2">
          Overrides for flagged tracks (checked in this order; leave blank to
          fall back to the template above)
        </p>
        <div className="mm-field-row">
          {OVERRIDES.map((override) => (
            <TrackNameTemplateField
              key={override.key}
              label={override.label}
              value={draft.track_name_overrides[override.key]}
              disabled={disabled}
              sampleFlags={override.flags}
              onChange={(value) =>
                change("track_name_overrides", {
                  ...draft.track_name_overrides,
                  [override.key]: value,
                })
              }
            />
          ))}
        </div>
      </div>
    </div>
  );
}

/** Consistent names for kept tracks, and the chapter list. */
export function TrackNamingFold({ binding }: { binding: RuleSetBinding }) {
  const { draft } = binding;
  return (
    <RuleFold
      title="Track naming and chapters"
      detail="Give kept tracks consistent names, and drop the container's chapter list."
      on={countOn([
        draft.standardize_track_names,
        draft.clear_video_track_names,
        draft.remove_chapters,
      ])}
      of={3}
    >
      <RuleToggle
        binding={binding}
        name="standardize_track_names"
        label="Standardize audio and subtitle track names"
        detail="Write a name from the template below on every kept track, instead of whatever the release called it."
      />
      {draft.standardize_track_names ? (
        <TrackNameTemplates binding={binding} />
      ) : null}
      <RuleToggle
        binding={binding}
        name="clear_video_track_names"
        label="Clear video track names"
        detail='Blank out scene-tag video titles such as "x265-GROUP".'
      />
      <RuleToggle
        binding={binding}
        name="remove_chapters"
        label="Remove chapters"
        detail="Drop the container's chapter list."
      />
    </RuleFold>
  );
}
