/**
 * User-visible Refiner copy — media-agnostic framing; Radarr/Sonarr only where the action is library-specific.
 * Route paths and code identifiers stay technical; this module is for UI strings only.
 */

export const REFINER_PAGE_FRAMING_PRIMARY =
  "Refiner helps clean up failed movie and TV imports and shows background work MediaMop has queued.";

export const REFINER_RUNTIME_CARD_TITLE = "Loaded settings (read-only)";
export const REFINER_RUNTIME_CARD_SUBTITLE =
  "From saved configuration when this loaded — not proof that background work is running.";

export const REFINER_SCHEDULE_MOVIES_HEADING = "Movies (Radarr) — scheduled import cleanup";
export const REFINER_SCHEDULE_TV_HEADING = "TV shows (Sonarr) — scheduled import cleanup";

export const REFINER_MANUAL_QUEUE_SECTION_TITLE = "Queue import cleanup check";
export const REFINER_MANUAL_QUEUE_SECTION_BODY =
  "Adds or reuses one queued import-cleanup check per library connection. Nothing runs in this browser request; " +
  "this does not show whether a worker picked it up or finished.";

export const REFINER_MANUAL_QUEUE_BTN_MOVIES = "Movies (Radarr)";
export const REFINER_MANUAL_QUEUE_BTN_TV = "TV shows (Sonarr)";
export const REFINER_MANUAL_QUEUE_PENDING = "Adding to queue…";

export const REFINER_MANUAL_QUEUE_ERR_MOVIES = "Could not queue movies import check.";
export const REFINER_MANUAL_QUEUE_ERR_TV = "Could not queue TV import check.";

export const REFINER_MANUAL_QUEUE_RESULT_MOVIES_PREFIX = "Movies:";
export const REFINER_MANUAL_QUEUE_RESULT_TV_PREFIX = "TV shows:";

export const REFINER_PAGE_LOADING_JOBS = "Loading job list…";
export const REFINER_PAGE_ERR_LOAD_JOBS = "Could not load the job list for this page.";

export const REFINER_JOBS_SECTION_TITLE = "Background jobs";
