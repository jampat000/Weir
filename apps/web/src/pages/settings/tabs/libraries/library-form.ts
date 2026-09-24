/**
 * A processing library as its editor holds it: every number as the text in its field, dates as local
 * datetime-local values. `formFrom` and `writeFrom` convert to and from the API's shape.
 */
import type {
  ProcessingFailurePolicy,
  ProcessingLibrary,
  ProcessingLibraryWrite,
  ProcessingMediaType,
  RemuxWriter,
} from "../../../../lib/processing/libraries-api";

export type LibraryForm = {
  name: string;
  media_type: ProcessingMediaType;
  watched_folder: string;
  work_folder: string;
  output_folder: string;
  media_extensions_csv: string;
  exclude_markers_csv: string;
  include_patterns_csv: string;
  exclude_patterns_csv: string;
  min_file_size_mb: string;
  max_file_size_mb: string;
  rejected_file_action: "leave" | "delete_file";
  min_file_age_seconds: string;
  created_after: string;
  created_before: string;
  modified_after: string;
  modified_before: string;
  scan_interval_seconds: string;
  hold_minutes: string;
  file_detection_interval_seconds: string;
  max_concurrent_files: string;
  priority: string;
  sidecar_patterns_csv: string;
  output_collision_policy: string;
  hardware_decode_mode: string;
  hardware_device: string;
  hardware_disabled_vendors_csv: string;
  ffmpeg_strictness: string;
  max_attempts: string;
  retry_backoff_seconds: string;
  exclude_hidden: boolean;
  top_level_only: boolean;
  ignore_size_changes: boolean;
  skip_access_tests: boolean;
  file_system_events_enabled: boolean;
  preserve_original_timestamps: boolean;
  remove_original_after_success: boolean;
  retry_execution_failures: boolean;
  retry_preflight_failures: boolean;
  failure_policy: ProcessingFailurePolicy;
  schedule_grid: string;
  rule_set_id: string;
  /** The one media manager this library is linked to, as its connection id; "" for none. */
  manager_connection_id: string;
  remux_writer: RemuxWriter;
};

type KeysOf<Value> = {
  [Key in keyof LibraryForm]: LibraryForm[Key] extends Value ? Key : never;
}[keyof LibraryForm];

/** The fields that hold text, which is every number too. */
export type LibraryTextField = KeysOf<string>;
/** The fields that are a checkbox. */
export type LibraryToggleField = KeysOf<boolean>;

/** A new library's values; the numbers are the server's own defaults. */
export const EMPTY_LIBRARY_FORM: LibraryForm = {
  name: "",
  media_type: "movie",
  watched_folder: "",
  work_folder: "",
  output_folder: "",
  media_extensions_csv: ".mkv,.mp4,.m4v,.webm,.avi",
  exclude_markers_csv:
    ".sabnzbd,__admin__,_failed_,_unpack_,_repair_,incomplete",
  include_patterns_csv: "",
  exclude_patterns_csv: "",
  min_file_size_mb: "0",
  max_file_size_mb: "0",
  rejected_file_action: "leave",
  min_file_age_seconds: "60",
  created_after: "",
  created_before: "",
  modified_after: "",
  modified_before: "",
  scan_interval_seconds: "300",
  hold_minutes: "0",
  file_detection_interval_seconds: "30",
  max_concurrent_files: "0",
  priority: "0",
  sidecar_patterns_csv: ".srt,.ass,.ssa,.sub,.idx,.vtt,.nfo,.jpg,.png",
  output_collision_policy: "replace",
  hardware_decode_mode: "off",
  hardware_device: "",
  hardware_disabled_vendors_csv: "",
  ffmpeg_strictness: "normal",
  max_attempts: "3",
  retry_backoff_seconds: "300",
  exclude_hidden: true,
  top_level_only: false,
  ignore_size_changes: false,
  skip_access_tests: false,
  file_system_events_enabled: true,
  preserve_original_timestamps: false,
  remove_original_after_success: true,
  retry_execution_failures: true,
  retry_preflight_failures: false,
  failure_policy: "pass_through",
  schedule_grid: "",
  rule_set_id: "",
  manager_connection_id: "",
  remux_writer: "best",
};

const MS_PER_MINUTE = 60_000;
/** "YYYY-MM-DDTHH:MM", the length a datetime-local value takes. */
const DATETIME_LOCAL_LENGTH = 16;

/** A stored UTC time as the local value a datetime-local input shows. */
function localDateTimeValue(value: string | null): string {
  if (!value) return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "";
  const local = new Date(
    parsed.getTime() - parsed.getTimezoneOffset() * MS_PER_MINUTE,
  );
  return local.toISOString().slice(0, DATETIME_LOCAL_LENGTH);
}

/** A datetime-local value as the UTC time the server stores, or null when blank. */
function utcDateTimeValue(value: string): string | null {
  if (!value.trim()) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

function wholeNumber(raw: string, fallback: number): number {
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : fallback;
}

const NUMBER_FIELDS = [
  "min_file_size_mb",
  "max_file_size_mb",
  "min_file_age_seconds",
  "scan_interval_seconds",
  "hold_minutes",
  "file_detection_interval_seconds",
  "max_concurrent_files",
  "priority",
  "max_attempts",
  "retry_backoff_seconds",
] as const satisfies readonly (keyof ProcessingLibrary & LibraryTextField)[];

const DATE_FIELDS = [
  "created_after",
  "created_before",
  "modified_after",
  "modified_before",
] as const satisfies readonly (keyof ProcessingLibrary & LibraryTextField)[];

const TEXT_FIELDS = [
  "name",
  "watched_folder",
  "work_folder",
  "output_folder",
  "media_extensions_csv",
  "exclude_markers_csv",
  "include_patterns_csv",
  "exclude_patterns_csv",
  "sidecar_patterns_csv",
  "hardware_device",
  "hardware_disabled_vendors_csv",
] as const satisfies readonly (keyof ProcessingLibrary & LibraryTextField)[];

/** The editor's values for a saved library. */
export function formFrom(library: ProcessingLibrary): LibraryForm {
  const form: LibraryForm = {
    ...EMPTY_LIBRARY_FORM,
    media_type: library.media_type,
    rejected_file_action: library.rejected_file_action,
    output_collision_policy: library.output_collision_policy,
    hardware_decode_mode: library.hardware_decode_mode,
    ffmpeg_strictness: library.ffmpeg_strictness,
    exclude_hidden: library.exclude_hidden,
    top_level_only: library.top_level_only,
    ignore_size_changes: library.ignore_size_changes,
    skip_access_tests: library.skip_access_tests,
    file_system_events_enabled: library.file_system_events_enabled,
    preserve_original_timestamps: library.preserve_original_timestamps,
    remove_original_after_success:
      library.remove_original_after_success ?? true,
    retry_execution_failures: library.retry_execution_failures,
    retry_preflight_failures: library.retry_preflight_failures,
    failure_policy: library.failure_policy,
    schedule_grid: library.schedule_grid,
    rule_set_id:
      library.rule_set_id === null ? "" : String(library.rule_set_id),
    manager_connection_id:
      library.manager_connection_ids.length > 0
        ? String(library.manager_connection_ids[0])
        : "",
    remux_writer: library.remux_writer ?? "best",
  };
  for (const key of TEXT_FIELDS) form[key] = library[key];
  for (const key of NUMBER_FIELDS) form[key] = String(library[key]);
  for (const key of DATE_FIELDS) form[key] = localDateTimeValue(library[key]);
  return form;
}

/** What the API is sent for these values; a saved library keeps the settings this editor does not show. */
export function writeFrom(
  form: LibraryForm,
  library?: ProcessingLibrary,
): ProcessingLibraryWrite {
  return {
    name: form.name.trim(),
    media_type: form.media_type,
    enabled: library?.enabled ?? true,
    watched_folder: form.watched_folder.trim(),
    work_folder: form.work_folder.trim(),
    output_folder: form.output_folder.trim(),
    media_extensions_csv: form.media_extensions_csv.trim(),
    exclude_markers_csv: form.exclude_markers_csv.trim(),
    include_patterns_csv: form.include_patterns_csv.trim(),
    exclude_patterns_csv: form.exclude_patterns_csv.trim(),
    min_file_size_mb: wholeNumber(form.min_file_size_mb, 0),
    max_file_size_mb: wholeNumber(form.max_file_size_mb, 0),
    rejected_file_action: form.rejected_file_action,
    min_file_age_seconds: wholeNumber(form.min_file_age_seconds, 60),
    created_after: utcDateTimeValue(form.created_after),
    created_before: utcDateTimeValue(form.created_before),
    modified_after: utcDateTimeValue(form.modified_after),
    modified_before: utcDateTimeValue(form.modified_before),
    scan_interval_seconds: wholeNumber(form.scan_interval_seconds, 300),
    hold_minutes: wholeNumber(form.hold_minutes, 0),
    file_detection_interval_seconds: wholeNumber(
      form.file_detection_interval_seconds,
      30,
    ),
    max_concurrent_files: wholeNumber(form.max_concurrent_files, 0),
    priority: wholeNumber(form.priority, 0),
    sidecar_patterns_csv: form.sidecar_patterns_csv.trim(),
    preserve_original_timestamps: form.preserve_original_timestamps,
    remove_original_after_success: form.remove_original_after_success,
    output_collision_policy: form.output_collision_policy,
    hardware_decode_mode: form.hardware_decode_mode,
    hardware_device: form.hardware_device.trim(),
    hardware_disabled_vendors_csv: form.hardware_disabled_vendors_csv.trim(),
    ffmpeg_strictness: form.ffmpeg_strictness,
    max_attempts: wholeNumber(form.max_attempts, 3),
    retry_backoff_seconds: wholeNumber(form.retry_backoff_seconds, 300),
    retry_execution_failures: form.retry_execution_failures,
    retry_preflight_failures: form.retry_preflight_failures,
    failure_policy: form.failure_policy,
    exclude_hidden: form.exclude_hidden,
    top_level_only: form.top_level_only,
    ignore_size_changes: form.ignore_size_changes,
    skip_access_tests: form.skip_access_tests,
    file_system_events_enabled: form.file_system_events_enabled,
    schedule_grid: form.schedule_grid,
    schedule_enabled: library?.schedule_enabled ?? true,
    schedule_hours_limited: library?.schedule_hours_limited ?? false,
    schedule_days: library?.schedule_days ?? "",
    schedule_start: library?.schedule_start ?? "00:00",
    schedule_end: library?.schedule_end ?? "23:59",
    rule_set_id: form.rule_set_id ? Number(form.rule_set_id) : null,
    // One manager per library from the editor (#651); a library linked to more than one through the API keeps
    // the others, so saving here never drops a link nobody chose to remove.
    manager_connection_ids: form.manager_connection_id
      ? [
          Number(form.manager_connection_id),
          ...(library?.manager_connection_ids ?? []).slice(1),
        ].filter((id, i, all) => all.indexOf(id) === i)
      : [],
    remux_writer: form.remux_writer,
  };
}
