/**
 * User-visible copy for the Fetcher page section: Radarr/Sonarr download-queue failed-import workflow.
 * (Refiner’s future stale-on-disk cleanup is a different concept — called out only where operators might confuse them.)
 */

export const FETCHER_FI_SECTION_INTRO_PRIMARY =
  "This section covers Radarr and Sonarr download-queue failed-import review and removal: what MediaMop recorded, the settings that apply, and optional manual starts.";

export const FETCHER_FI_SECTION_INTRO_SCOPE =
  "Each row reflects work that walked that app’s download queue, removed eligible failed-import entries when your policy allows, and recorded outcomes. That is queue work inside Radarr/Sonarr — not Refiner-style removal of stale files left on disk after importing finishes.";

export const FETCHER_FI_RUNTIME_CARD_TITLE = "Loaded settings (read-only)";
export const FETCHER_FI_RUNTIME_CARD_SUBTITLE =
  "From saved configuration when this loaded — not proof that in-process workers or timed passes are active right now.";

export const FETCHER_FI_RUNTIME_WORKERS_HEADING = "In-process background workers";
export const FETCHER_FI_RUNTIME_WORKER_COUNT_LABEL = "Workers (configured):";

export const FETCHER_FI_SCHEDULE_MOVIES_HEADING =
  "Movies (Radarr) — scheduled download-queue failed-import pass";
export const FETCHER_FI_SCHEDULE_TV_HEADING =
  "TV shows (Sonarr) — scheduled download-queue failed-import pass";

export const FETCHER_FI_MANUAL_SECTION_TITLE = "Manually start a failed-import pass";
export const FETCHER_FI_MANUAL_SECTION_BODY =
  "Adds or reuses one recorded row that will review that app’s download queue for failed-import entries and apply your removal policy. Nothing runs in this browser session; this does not show whether a worker picked it up or finished.";

export const FETCHER_FI_MANUAL_BTN_MOVIES = "Movies — Radarr queue";
export const FETCHER_FI_MANUAL_BTN_TV = "TV shows — Sonarr queue";
export const FETCHER_FI_MANUAL_PENDING = "Recording request…";

export const FETCHER_FI_MANUAL_ERR_MOVIES = "Could not record the movies failed-import pass.";
export const FETCHER_FI_MANUAL_ERR_TV = "Could not record the TV failed-import pass.";

export const FETCHER_FI_MANUAL_RESULT_MOVIES_PREFIX = "Movies (Radarr):";
export const FETCHER_FI_MANUAL_RESULT_TV_PREFIX = "TV shows (Sonarr):";

export const FETCHER_FI_PAGE_LOADING_TASKS = "Loading recorded work…";
export const FETCHER_FI_PAGE_ERR_LOAD_TASKS = "Could not load the list for this section.";

export const FETCHER_FI_TASKS_SECTION_TITLE = "Recorded work";

export const FETCHER_FI_FILTER_DEFAULT_HELP = "Showing the default: finished outcomes only.";
export const FETCHER_FI_FILTER_SINGLE_STATUS_HELP =
  "Filtered to one stored status — see the Status column for the exact value.";

export const FETCHER_FI_TABLE_COL_WORK_TYPE = "Type of work";
export const FETCHER_FI_TABLE_COL_STABLE_KEY = "Stable key";

export const FETCHER_FI_LIST_EMPTY = "Nothing matches this view.";
