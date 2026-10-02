/** The four workflows and their rules: a Weir install on a media PC with a manager for most of its libraries. */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, DAY_MS } from "../wire-time.mjs";
import {
  FOUR_K_MANAGER_ID,
  MOVIES_MANAGER_ID,
  TV_MANAGER_ID,
} from "./connections.mjs";

export const MOVIES_LIBRARY_ID = 1;
export const TV_LIBRARY_ID = 2;
export const KIDS_LIBRARY_ID = 3;
export const FOUR_K_LIBRARY_ID = 4;

const MOVIES_RULES_ID = 1;
const TV_RULES_ID = 2;
const KIDS_RULES_ID = 3;

const UPDATED_DAYS_AGO = 12;

/** Where passes write while they work: most workflows use the system drive, and the 4K one a drive of its own. */
const SYSTEM_WORK_FOLDER = "D:\\Weir\\work";
const SPARE_DRIVE_WORK_FOLDER = "E:\\Weir\\work";

/** What a workflow starts with; a new one made from Settings begins here too. */
export const libraryDefaults = () => ({
  enabled: true,
  exclude_hidden: true,
  top_level_only: false,
  media_extensions_csv: ".mkv,.mp4,.avi,.m4v",
  exclude_markers_csv: "sample,trailer",
  failure_policy: "hold",
  ffmpeg_strictness: "normal",
  output_collision_policy: "replace",
  rejected_file_action: "leave",
  remux_writer: "best",
  ready_after_seconds: 60,
  scan_interval_seconds: 300,
  max_attempts: 3,
  retry_backoff_seconds: 300,
  retry_execution_failures: true,
  retry_preflight_failures: true,
  file_system_events_enabled: true,
  schedule_enabled: true,
  schedule_hours_limited: false,
  schedule_days: "",
  schedule_start: "00:00",
  schedule_end: "23:59",
  schedule_grid: "",
  max_concurrent_files: 0,
  effective_max_concurrent_files: 2,
  minimum_free_disk_space_mb: 20480,
  manager_coverage: "connected",
  manager_coverage_detail:
    "The linked manager connection is healthy. Upstream checks and manager-truth-dependent cleanup can use its latest answer.",
  periodic_scan: "scheduled",
  remove_original_after_success: false,
  updated_at: toWire(Date.now() - UPDATED_DAYS_AGO * DAY_MS),
});

const workflow = (fields) =>
  shaped("ProcessingLibraryOut", { ...libraryDefaults(), ...fields });

export function initialLibraries() {
  return [
    workflow({
      id: MOVIES_LIBRARY_ID,
      name: "Movies",
      media_type: "movie",
      display_order: 1,
      watched_folder: "D:\\Downloads\\Movies",
      work_folder: SYSTEM_WORK_FOLDER,
      output_folder: "D:\\Weir\\hand-back\\Movies",
      rule_set_id: MOVIES_RULES_ID,
      manager_connection_ids: [MOVIES_MANAGER_ID],
    }),
    workflow({
      id: TV_LIBRARY_ID,
      name: "TV",
      media_type: "tv",
      display_order: 2,
      watched_folder: "D:\\Downloads\\TV",
      work_folder: SYSTEM_WORK_FOLDER,
      output_folder: "D:\\Weir\\hand-back\\TV",
      rule_set_id: TV_RULES_ID,
      manager_connection_ids: [TV_MANAGER_ID],
    }),
    workflow({
      id: KIDS_LIBRARY_ID,
      name: "Kids",
      media_type: "movie",
      display_order: 3,
      watched_folder: "D:\\Downloads\\Kids",
      work_folder: SYSTEM_WORK_FOLDER,
      output_folder: "D:\\Weir\\hand-back\\Kids",
      rule_set_id: KIDS_RULES_ID,
      manager_connection_ids: [],
    }),
    workflow({
      id: FOUR_K_LIBRARY_ID,
      name: "4K Movies",
      media_type: "movie",
      display_order: 4,
      watched_folder: "D:\\Downloads\\4K Movies",
      work_folder: SPARE_DRIVE_WORK_FOLDER,
      output_folder: "D:\\Weir\\hand-back\\4K Movies",
      rule_set_id: MOVIES_RULES_ID,
      manager_connection_ids: [FOUR_K_MANAGER_ID],
    }),
  ];
}

/** The download client kinds whose own folders a starter workflow's folder chain reads; a workflow made from Settings uses its media type's usual one. */
const CLIENT_KINDS = {
  [MOVIES_LIBRARY_ID]: ["qbittorrent"],
  [TV_LIBRARY_ID]: ["sabnzbd"],
  [KIDS_LIBRARY_ID]: [],
  [FOUR_K_LIBRARY_ID]: [],
};

/** @param {{ id: number, media_type: string }} library */
export const downloadClientKindsOf = (library) =>
  CLIENT_KINDS[library.id] ??
  (library.media_type === "tv" ? ["sabnzbd"] : ["qbittorrent"]);

/** What a rule set starts with; a new one made from Settings begins here too. */
export const ruleSetDefaults = () => ({
  primary_audio_lang: "eng",
  secondary_audio_lang: "",
  tertiary_audio_lang: "",
  default_audio_slot: "primary",
  remove_commentary: true,
  subtitle_mode: "keep_listed",
  subtitle_langs_csv: "eng",
  preserve_forced_subs: true,
  preserve_default_subs: true,
  audio_preference_mode: "preferred_langs_quality",
  keep_original_language: false,
  original_language_first_if_none: true,
  remove_images: true,
  remove_attachments: true,
  remove_title: true,
  remove_other_metadata: true,
  remove_hearing_impaired_subs: true,
  audio_keep_mode: "single",
  subtitle_quality_strategy: "text_first",
  track_name_template: "{language}{variant}",
  track_name_overrides: {
    forced: "Forced",
    hearing_impaired: "SDH",
    commentary: "Commentary",
    audio_description: "Audio description",
  },
  clear_video_track_names: true,
  updated_at: toWire(Date.now() - UPDATED_DAYS_AGO * DAY_MS),
});

const ruleSet = (id, name, usedBy) =>
  shaped("ProcessingRuleSetOut", {
    ...ruleSetDefaults(),
    id,
    name,
    used_by_library_count: usedBy,
  });

export function initialRuleSets() {
  return [
    ruleSet(MOVIES_RULES_ID, "Movies rules", 2),
    ruleSet(TV_RULES_ID, "TV rules", 1),
    ruleSet(KIDS_RULES_ID, "Kids rules", 1),
  ];
}
