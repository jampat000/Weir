/**
 * Library mode (#505): clean files already in a library, in place. Weir server (.NET) only — there is no
 * Python backend route to match (see docs/archive/server-port-notes.md, "Library mode").
 */
import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson, requireOk } from "../api/client";

export type LibraryFileClassification =
  "matches" | "would_change" | "cannot_process";

export interface LibrarySettings {
  library_folders: string[];
  library_schedule_enabled: boolean;
  /** #508 step 1: clean a file even while another name still shares its data (seeding). Default false. */
  clean_hardlinked_files: boolean;
  /** #508 step 2: skip a clean that would make a manager re-download the title. Default true. Not yet enforced
   * server-side — see docs/archive/server-port-notes.md's "Seams for #507, #508 and #509" — but always safe to save. */
  skip_if_manager_would_redownload: boolean;
}

/**
 * Issue #568: the facets the Library view breaks a library down by and filters its Files table on. Same names
 * the server stores and accepts, so a "show files" link is just this value in the query string.
 */
export const LIBRARY_FACETS = [
  "video_codec",
  "resolution",
  "audio",
  "audio_language",
  "subtitle_language",
] as const;

export type LibraryFacet = (typeof LIBRARY_FACETS)[number];

/** Why a file is not something Weir will clean (#568's Problems view). */
export type LibraryProblemKind =
  | "seeding"
  | "manager_redownload"
  | "no_permission"
  | "unreadable"
  | "no_video"
  | "no_audio_left";

/** The Files table's sortable columns, as the server names them. */
export const LIBRARY_FILE_SORTS = [
  "path",
  "title",
  "size",
  "state",
  "saved",
  "video",
  "resolution",
  "audio",
  "subtitles",
  "modified",
] as const;

export type LibraryFileSort = (typeof LIBRARY_FILE_SORTS)[number];

export interface LibraryFile {
  path: string;
  size_bytes: number;
  modified_at: number;
  classification: LibraryFileClassification;
  summary: string | null;
  reason: string | null;
  removed_audio_tracks: number;
  removed_subtitle_tracks: number;
  estimated_bytes_saved: number;
  manager_kind: string | null;
  manager_title: string | null;
  /** #568's media facts, from the ffprobe JSON the scan cached; "unknown" when it did not carry one. */
  video_codec: string;
  video_height: number | null;
  resolution_class: string;
  audio_track_count: number;
  subtitle_track_count: number;
  audio_summary: string | null;
  subtitle_summary: string | null;
  link_count: number | null;
  problem_kind: LibraryProblemKind | null;
  /** When Weir last cleaned this file (unix seconds), or null if it never has. Outlives a rescan. */
  cleaned_at: number | null;
  /** You asked Weir to leave this file alone; nothing cleans it until you say otherwise. */
  leave_alone: boolean;
}

export interface LibraryTotals {
  files: number;
  size_bytes: number;
  matches: number;
  would_change: number;
  cannot_process: number;
  total_removed_audio_tracks: number;
  total_removed_subtitle_tracks: number;
  estimated_bytes_saved: number;
  /** Files Weir has cleaned at least once, and files you have set aside. */
  cleaned: number;
  left_alone: number;
}

export interface LibraryScanInfo {
  job_id: number | null;
  status: string;
  /** Queued or being worked on right now — what "Scan now" shows progress for. */
  running: boolean;
  generated_at: number | null;
  errors: string[];
}

export interface LibraryBreakdownRow {
  value: string;
  files: number;
  size_bytes: number;
  /** 0-1, computed on the server so every bar is drawn from one number. */
  share: number;
}

export type LibraryBreakdowns = Record<LibraryFacet, LibraryBreakdownRow[]>;

export interface LibraryProblemGroup {
  kind: LibraryProblemKind;
  title: string;
  what_to_do: string;
  files: number;
  size_bytes: number;
  sample_paths: string[];
}

/**
 * "Scheduled scan and clean": once a day, inside the library's own schedule window. `next_run_at` is null when it
 * is off or cannot run (no library folders, the library switched off, a window that never opens).
 */
export interface LibraryModeSchedule {
  enabled: boolean;
  next_run_at: string | null;
}

export interface LibraryOverview {
  library_id: number;
  folders_configured: number;
  scan: LibraryScanInfo | null;
  schedule: LibraryModeSchedule;
  totals: LibraryTotals;
  breakdowns: LibraryBreakdowns;
  problems: LibraryProblemGroup[];
}

/** Every way the Files table can be narrowed, sorted and paged. */
export interface LibraryFileFilters {
  classification?: LibraryFileClassification;
  manager?: string;
  q?: string;
  problem?: LibraryProblemKind;
  /** What Weir has done with the file, rather than what is in it. */
  state?: "cleaned" | "left_alone";
  facets?: Partial<Record<LibraryFacet, string>>;
  sort?: LibraryFileSort;
  direction?: "asc" | "desc";
  page?: number;
  page_size?: number;
}

export interface LibraryFilesResult {
  library_id: number;
  scan: LibraryScanInfo | null;
  /** The whole library, whatever the filters say — what the header reads. */
  summary: LibraryTotals;
  /** Just what the current filters select. */
  filtered: LibraryTotals;
  files: LibraryFile[];
  total: number;
  page: number;
  page_size: number;
  sort: LibraryFileSort;
  direction: "asc" | "desc";
}

export interface LibraryScanTrigger {
  job_id: number;
  status: string;
  already_running: boolean;
}

/** The exact #505 point 5 confirmation: "N files, M tracks will be removed..." plus the size behind it. */
export interface LibraryConfirmationRequired {
  kind: "confirmation_required";
  detail: string;
  files_count: number;
  tracks_count: number;
  estimated_bytes_saved: number;
  /** #508's per-file preflight notes (seeding, re-download risk), one line per file that would be skipped. */
  warnings?: string[];
}

export interface LibraryCleanResult {
  kind: "cleaned";
  queued: number;
  job_ids: number[];
  files_count: number;
  tracks_count: number;
  estimated_bytes_saved: number;
  /** #508: paths selected for cleaning but skipped outright (still shared with a download). */
  skipped_paths: string[];
  /** #508's per-file preflight notes, same shape as the confirmation dialog's. */
  warnings: string[];
}

function librarySettingsPath(libraryId: number): string {
  return `/api/v1/processing/libraries/${libraryId}/library-settings`;
}

export async function fetchLibrarySettings(
  libraryId: number,
): Promise<LibrarySettings> {
  const path = librarySettingsPath(libraryId);
  const r = await apiFetch(path);
  await requireOk(
    path,
    r,
    "Could not load this library's library-mode settings",
  );
  return readJson<LibrarySettings>(r);
}

export async function saveLibraryFolders(
  libraryId: number,
  library_folders: string[],
): Promise<LibrarySettings> {
  return saveLibrarySettings(libraryId, { library_folders });
}

/**
 * PUTs library-mode settings. `library_folders` is required on every call (the API replaces the whole list, not
 * just the fields sent, when it is present — an absent list is read as "no folders", not "leave unchanged"), so a
 * checkbox-only save must still pass the library's current folders. `clean_hardlinked_files` and
 * `skip_if_manager_would_redownload` are each optional and keep their saved value when left out.
 */
export async function saveLibrarySettings(
  libraryId: number,
  updates: {
    library_folders: string[];
    clean_hardlinked_files?: boolean;
    skip_if_manager_would_redownload?: boolean;
  },
): Promise<LibrarySettings> {
  const csrf_token = await fetchCsrfToken();
  const path = librarySettingsPath(libraryId);
  const r = await apiFetch(path, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...updates, csrf_token }),
  });
  await requireOk(path, r, "Could not save this library's settings");
  return readJson<LibrarySettings>(r);
}

export async function triggerLibraryScan(
  libraryId: number,
): Promise<LibraryScanTrigger> {
  const csrf_token = await fetchCsrfToken();
  const path = `/api/v1/processing/libraries/${libraryId}/library-scan`;
  const r = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ csrf_token }),
  });
  await requireOk(path, r, "Could not start a library scan");
  return readJson<LibraryScanTrigger>(r);
}

/** Turns the Files table's filters into the query string the server reads them from. */
export function libraryFileFiltersToParams(
  filters: LibraryFileFilters,
): URLSearchParams {
  const params = new URLSearchParams();
  if (filters.classification)
    params.set("classification", filters.classification);
  if (filters.manager) params.set("manager", filters.manager);
  if (filters.q) params.set("q", filters.q);
  if (filters.problem) params.set("problem", filters.problem);
  if (filters.state) params.set("state", filters.state);
  for (const facet of LIBRARY_FACETS) {
    const value = filters.facets?.[facet];
    if (value) params.set(facet, value);
  }
  if (filters.sort) params.set("sort", filters.sort);
  if (filters.direction) params.set("direction", filters.direction);
  if (filters.page && filters.page > 1)
    params.set("page", String(filters.page));
  if (filters.page_size) params.set("page_size", String(filters.page_size));
  return params;
}

export async function fetchLibraryFiles(
  libraryId: number,
  filters: LibraryFileFilters = {},
): Promise<LibraryFilesResult> {
  const suffix = libraryFileFiltersToParams(filters).toString();
  const path = `/api/v1/processing/libraries/${libraryId}/library-files${suffix ? `?${suffix}` : ""}`;
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load this library's files");
  return readJson<LibraryFilesResult>(r);
}

/** #568's Overview: totals and every breakdown, aggregated on the server. */
export async function fetchLibraryOverview(
  libraryId: number,
): Promise<LibraryOverview> {
  const path = `/api/v1/processing/libraries/${libraryId}/library-overview`;
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load this library's overview");
  return readJson<LibraryOverview>(r);
}

/** Parses the structured 400 the API returns when removal needs confirming. Rethrows anything else. */
async function readConfirmationOr<T>(
  path: string,
  r: Response,
  fallback: string,
): Promise<LibraryConfirmationRequired | T> {
  if (r.status === 400) {
    const body = await readJson<Record<string, unknown>>(r);
    if (body && body.error === "confirm_final_removal_required") {
      return {
        kind: "confirmation_required",
        detail: String(body.detail ?? ""),
        files_count: Number(body.files_count ?? 0),
        tracks_count: Number(body.tracks_count ?? 0),
        estimated_bytes_saved: Number(body.estimated_bytes_saved ?? 0),
        warnings: Array.isArray(body.warnings)
          ? body.warnings.map(String)
          : undefined,
      };
    }
  }
  await requireOk(path, r, fallback);
  return readJson<T>(r);
}

/**
 * Your own choice of tracks for one file (#501's shape), sent with a clean instead of leaving it to the library's
 * rules. `expected_size_bytes` is the size the file had when you chose: Weir refuses the clean rather than apply
 * the choice to a file that has changed since.
 */
export interface LibraryManualPlan {
  keep: { index: number; default: boolean; forced: boolean }[];
  order: number[];
  expected_size_bytes?: number;
}

export async function cleanLibraryFiles(
  libraryId: number,
  paths: string[],
  confirm_final_removal: boolean,
  manual?: LibraryManualPlan,
): Promise<LibraryConfirmationRequired | LibraryCleanResult> {
  const csrf_token = await fetchCsrfToken();
  const path = `/api/v1/processing/libraries/${libraryId}/library-files/clean`;
  const r = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      paths,
      confirm_final_removal,
      csrf_token,
      ...(manual
        ? {
            manual_plan: { keep: manual.keep, order: manual.order },
            ...(manual.expected_size_bytes === undefined
              ? {}
              : { expected_size_bytes: manual.expected_size_bytes }),
          }
        : {}),
    }),
  });
  const result = await readConfirmationOr<Omit<LibraryCleanResult, "kind">>(
    path,
    r,
    "Could not queue those files to clean",
  );
  return "kind" in result ? result : { kind: "cleaned", ...result };
}

/** "Leave this file alone" and its undo; a rescan does not forget it. */
export async function setLibraryFileLeaveAlone(
  libraryId: number,
  filePath: string,
  leave_alone: boolean,
): Promise<{ path: string; leave_alone: boolean }> {
  const csrf_token = await fetchCsrfToken();
  const path = `/api/v1/processing/libraries/${libraryId}/library-files/leave-alone`;
  const r = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ path: filePath, leave_alone, csrf_token }),
  });
  await requireOk(path, r, "Could not change that file");
  return readJson<{ path: string; leave_alone: boolean }>(r);
}

export async function setLibrarySchedule(
  libraryId: number,
  enabled: boolean,
  confirm_final_removal: boolean,
): Promise<LibraryConfirmationRequired | LibrarySettings> {
  const csrf_token = await fetchCsrfToken();
  const path = `/api/v1/processing/libraries/${libraryId}/library-schedule`;
  const r = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ enabled, confirm_final_removal, csrf_token }),
  });
  return readConfirmationOr<LibrarySettings>(
    path,
    r,
    "Could not change the library schedule",
  );
}
