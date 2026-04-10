/**
 * User-visible Refiner copy — media-first framing; Radarr/Sonarr only where the integration is fixed.
 * Failed-import removal in *arr download queues is named explicitly and kept separate from stale-file-on-disk cleanup.
 */

export const REFINER_PAGE_FRAMING_PRIMARY =
  "Refiner helps you refine movie and TV libraries. This page lists recorded tasks and the settings behind them.";

/** Scope boundary: *arr download-queue failed imports vs not disk/stale-file cleanup. */
export const REFINER_PAGE_FRAMING_SCOPE =
  "Built-in tasks today talk to Radarr or Sonarr: they review each app’s download queue for failed-import rows and remove eligible ones when your policy allows. That is queue work inside the app — not orphan or stale-file cleanup on disk, which Refiner does not perform here.";

export const REFINER_RUNTIME_CARD_TITLE = "Loaded settings (read-only)";
export const REFINER_RUNTIME_CARD_SUBTITLE =
  "From saved configuration when this loaded — not proof that background runners or timed passes are active.";

export const REFINER_RUNTIME_RUNNERS_HEADING = "Background runners";
export const REFINER_RUNTIME_RUNNER_COUNT_LABEL = "Runners (configured):";

export const REFINER_SCHEDULE_MOVIES_HEADING =
  "Movies (Radarr) — scheduled download-queue failed-import pass";
export const REFINER_SCHEDULE_TV_HEADING =
  "TV shows (Sonarr) — scheduled download-queue failed-import pass";

export const REFINER_MANUAL_QUEUE_SECTION_TITLE = "Queue failed-import download-queue pass";
export const REFINER_MANUAL_QUEUE_SECTION_BODY =
  "Adds or reuses one recorded task that will review that app’s download queue for failed-import rows and apply your removal policy. Nothing runs in this browser session; this does not show whether a runner started or finished.";

export const REFINER_MANUAL_QUEUE_BTN_MOVIES = "Movies — Radarr queue";
export const REFINER_MANUAL_QUEUE_BTN_TV = "TV shows — Sonarr queue";
export const REFINER_MANUAL_QUEUE_PENDING = "Adding to queue…";

export const REFINER_MANUAL_QUEUE_ERR_MOVIES = "Could not queue the movies failed-import pass.";
export const REFINER_MANUAL_QUEUE_ERR_TV = "Could not queue the TV failed-import pass.";

export const REFINER_MANUAL_QUEUE_RESULT_MOVIES_PREFIX = "Movies (Radarr):";
export const REFINER_MANUAL_QUEUE_RESULT_TV_PREFIX = "TV shows (Sonarr):";

export const REFINER_PAGE_LOADING_JOBS = "Loading task list…";
export const REFINER_PAGE_ERR_LOAD_JOBS = "Could not load the task list for this page.";

export const REFINER_JOBS_SECTION_TITLE = "Recorded tasks";

export const REFINER_FILTER_DEFAULT_HELP = "Showing the default: finished tasks only.";
export const REFINER_FILTER_SINGLE_STATUS_HELP =
  "Filtered to one stored status — see the Status column for the exact value.";

export const REFINER_TABLE_COL_TASK_KIND = "Task kind";
export const REFINER_TABLE_COL_UNIQUENESS_KEY = "Uniqueness key";
