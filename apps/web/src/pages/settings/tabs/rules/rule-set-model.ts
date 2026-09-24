import type { ProcessingRuleSetWrite } from "../../../../lib/processing/rule-sets-api";
import { DEFAULT_TRACK_NAME_TEMPLATE } from "../../../../lib/processing/track-name-preview";

/** One criterion in a track order: which field, an optional value it must match, and its direction. */
export type TrackSorter = {
  field: string;
  value: string;
  reversed: boolean;
};

/** Every criterion the server ranks tracks by, content_tier (#497) included, so a save never drops one. */
export const SORTER_FIELDS = [
  "language",
  "channels",
  "codec",
  "bitrate",
  "title",
  "default",
  "forced",
  "commentary",
  "content_tier",
];

export const SORTER_LABELS: Record<string, string> = {
  language: "Language",
  channels: "Channel count",
  codec: "Codec",
  bitrate: "Bitrate",
  title: "Track title",
  default: "Default flag",
  forced: "Forced flag",
  commentary: "Commentary flag",
  content_tier: "Main, then dubs, then commentary",
};

export const DEFAULT_AUDIO_SORTERS: TrackSorter[] = [
  { field: "commentary", value: "", reversed: false },
  { field: "channels", value: "", reversed: false },
  { field: "codec", value: "", reversed: false },
  { field: "bitrate", value: "", reversed: false },
  { field: "default", value: "", reversed: false },
];

export const DEFAULT_SUBTITLE_SORTERS: TrackSorter[] = [
  { field: "forced", value: "", reversed: false },
  { field: "default", value: "", reversed: false },
  { field: "language", value: "", reversed: false },
];

/** The audio order for a selection strategy: quality across languages never ranks by the default flag. */
export function defaultAudioSortersFor(mode: string): TrackSorter[] {
  return mode === "quality_all_languages"
    ? DEFAULT_AUDIO_SORTERS.filter((row) => row.field !== "default")
    : DEFAULT_AUDIO_SORTERS;
}

const OVERRIDE_TEMPLATE = "{language} {flags}";

/** A new profile: English first, nothing removed that a person would miss. */
export const EMPTY_RULE_SET: ProcessingRuleSetWrite = {
  name: "",
  primary_audio_lang: "eng",
  secondary_audio_lang: "",
  tertiary_audio_lang: "",
  default_audio_slot: "primary",
  remove_commentary: true,
  subtitle_mode: "keep_all",
  subtitle_langs_csv: "",
  preserve_forced_subs: true,
  preserve_default_subs: true,
  audio_preference_mode: "preferred_langs_quality",
  audio_sorters_json: JSON.stringify(DEFAULT_AUDIO_SORTERS),
  subtitle_sorters_json: JSON.stringify(DEFAULT_SUBTITLE_SORTERS),
  keep_original_language: false,
  original_language_additional_csv: "",
  original_language_keep_only_first: true,
  original_language_first_if_none: true,
  original_language_treat_empty_as_original: false,
  remove_images: false,
  remove_attachments: false,
  remove_title: false,
  remove_language_tags: false,
  remove_other_metadata: false,
  remove_hearing_impaired_subs: false,
  audio_keep_mode: "single",
  subtitle_max_per_language: 0,
  subtitle_quality_strategy: "text_first",
  standardize_track_names: false,
  track_name_template: DEFAULT_TRACK_NAME_TEMPLATE,
  track_name_overrides: {
    forced: OVERRIDE_TEMPLATE,
    hearing_impaired: OVERRIDE_TEMPLATE,
    commentary: OVERRIDE_TEMPLATE,
    audio_description: OVERRIDE_TEMPLATE,
  },
  clear_video_track_names: false,
  remove_chapters: false,
};

/** Whether a profile's draft still matches what it would compare against (its saved form, or the empty starting point). */
export function sameRuleSet(
  a: ProcessingRuleSetWrite,
  b: ProcessingRuleSetWrite,
): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

function copyOf(rows: TrackSorter[]): TrackSorter[] {
  return rows.map((item) => ({ ...item }));
}

/** The saved order, or the fallback when it is empty, unreadable or names nothing the server knows. */
export function parseSorters(
  raw: string,
  fallback: TrackSorter[],
): TrackSorter[] {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return copyOf(fallback);
  }
  if (!Array.isArray(parsed)) return copyOf(fallback);
  const rows = parsed
    .filter(
      (item): item is Record<string, unknown> =>
        Boolean(item) && typeof item === "object",
    )
    .map((item) => ({
      field: String(item.field ?? "language"),
      value: typeof item.value === "string" ? item.value : "",
      reversed: Boolean(item.reversed),
    }))
    .filter((item) => SORTER_FIELDS.includes(item.field));
  return rows.length > 0 ? rows : copyOf(fallback);
}

/** The order as the server stores it: a blank match value is null. */
export function dumpSorters(rows: TrackSorter[]): string {
  return JSON.stringify(
    rows.map((row) => ({
      field: row.field,
      value: row.value.trim() || null,
      reversed: row.reversed,
    })),
  );
}

/** "eng, JPN ,," as ["eng", "jpn"]. */
export function csvValues(value: string): string[] {
  return value
    .split(",")
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);
}
