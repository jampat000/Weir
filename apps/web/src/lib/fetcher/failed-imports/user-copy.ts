/** Copy for the Fetcher failed-imports section only. */

import type { FailedImportQueueHandlingAction } from "./types";

export const FETCHER_FI_SECTION_INTRO_PRIMARY =
  "Failed imports are downloads Sonarr or Radarr could not import from the queue. " +
  "Use the sections below to choose actions, see what needs a look in the apps, and run checks.";

/** Manual checks and history card on the Failed imports tab. */
export const FETCHER_FI_MANUAL_UTILITY_SECTION_TITLE = "Manual checks and history";

export const FETCHER_FI_JOB_HISTORY_SUBSECTION_TITLE = "Job history";

export const FETCHER_FI_AT_A_GLANCE_SECTION_TITLE = "Failed imports at a glance";

export const FETCHER_FI_NEEDS_ATTENTION_SECTION_TITLE = "Needs attention now";

export const FETCHER_FI_NEEDS_ATTENTION_LEAD =
  "Shows queue rows that match one of the handled failure classes (below), not waiting-only or unrecognized text, " +
  "and are set to an action other than Leave alone. Open Sonarr or Radarr to inspect them.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_NOT_SETUP = "Set up Sonarr to check failed imports here.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_NOT_SETUP = "Set up Radarr to check failed imports here.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_CANT_CHECK = "Review your connection settings and try again.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_REVIEW = "Open Sonarr and review items in the download queue.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_REVIEW = "Open Radarr and review items in the download queue.";

export const FETCHER_FI_NEEDS_ATTENTION_SUPPORT_NONE = "Nothing matched your saved rules on the last check.";

export const FETCHER_FI_JOB_HISTORY_LEAD =
  "Filter by status. Open Details for a short explanation or raw error text.";

export const FETCHER_FI_MANUAL_SECTION_TITLE = "Run a check now";
export const FETCHER_FI_MANUAL_SECTION_BODY =
  "Runs one queue pass for that app using the actions you saved under Per-class queue actions.";

export const FETCHER_FI_MANUAL_BTN_MOVIES = "Radarr (Movies)";
export const FETCHER_FI_MANUAL_BTN_TV = "Sonarr (TV)";
export const FETCHER_FI_MANUAL_PENDING = "Sending…";

export const FETCHER_FI_MANUAL_ERR_MOVIES = "Could not start the movies check.";
export const FETCHER_FI_MANUAL_ERR_TV = "Could not start the TV check.";

export const FETCHER_FI_MANUAL_RESULT_MOVIES_PREFIX = "Radarr (Movies):";
export const FETCHER_FI_MANUAL_RESULT_TV_PREFIX = "Sonarr (TV):";

export const FETCHER_FI_PAGE_LOADING_TASKS = "Loading Fetcher jobs…";
export const FETCHER_FI_PAGE_ERR_LOAD_TASKS = "Could not load Fetcher jobs.";

export const FETCHER_FI_TASKS_SECTION_TITLE = "Job history";

export const FETCHER_FI_JOB_HISTORY_SHOW_LABEL = "Show";

export const FETCHER_FI_FILTER_DEFAULT_HELP =
  "Use Show for finished runs, errors, or jobs that need follow-up.";

export const FETCHER_FI_FILTER_SINGLE_STATUS_HELP =
  "Showing one outcome. Use Show to choose a different view.";

export const FETCHER_FI_TABLE_COL_WORK_TYPE = "Job type";
export const FETCHER_FI_TABLE_COL_STABLE_KEY = "Stable id";

export const FETCHER_FI_LIST_EMPTY = "Nothing matches this view.";

export const FETCHER_FI_TECHNICAL_SUMMARY_LABEL = "Technical detail";

export const FETCHER_FI_POLICY_CARD_TITLE = "Per-class queue actions";
export const FETCHER_FI_POLICY_CARD_LEAD =
  "Choose what Fetcher should do when Sonarr or Radarr cannot import a download.";
export const FETCHER_FI_POLICY_CARD_LEAD_SECOND =
  "Saving does not run anything here; the next automatic or manual pass applies these settings.";
export const FETCHER_FI_POLICY_QUEUE_ACTIONS_SUBHEADING = "Queue actions";
export const FETCHER_FI_POLICY_AUTOMATIC_CHECK_SUBHEADING = "Automatic check interval";
export const FETCHER_FI_POLICY_OPTIONS_GROUP_LABEL = "When this import failure is detected";

/** Empty-state line under the summary when every class is leave alone. */
export const FETCHER_FI_POLICY_NO_OPTIONS_ON = "Every class is set to leave alone.";

export const FETCHER_FI_POLICY_VIEWER_NOTE = "Sign in as an operator to change these options.";
export const FETCHER_FI_POLICY_SAVE = "Save queue action settings";
export const FETCHER_FI_POLICY_SAVE_SONARR = "Save Sonarr queue actions";
export const FETCHER_FI_POLICY_SAVE_RADARR = "Save Radarr queue actions";
export const FETCHER_FI_POLICY_SAVING = "Saving…";
export const FETCHER_FI_POLICY_SAVED_HINT = "Nothing runs immediately when you save.";
export const FETCHER_FI_POLICY_MOVIES_HEADING = "Radarr (Movies)";
export const FETCHER_FI_POLICY_TV_HEADING = "Sonarr (TV)";

/** Short row labels on the policy page — one row per **policy-backed** terminal class (six of eight enum outcomes). */
export const FETCHER_FI_POLICY_ROW_QUALITY = {
  primary: "Quality issue",
  support: "Not an upgrade or rejected quality",
} as const;

export const FETCHER_FI_POLICY_ROW_MANUAL_IMPORT = {
  primary: "Manual import required",
  support: "The app could not import it automatically",
} as const;

export const FETCHER_FI_POLICY_ROW_SAMPLE = {
  primary: "Sample / junk release",
  support: "Sample or obvious junk release detected",
} as const;

export const FETCHER_FI_POLICY_ROW_CORRUPT = {
  primary: "Corrupt / integrity failure",
  support: "Corrupt file or failed integrity check",
} as const;

export const FETCHER_FI_POLICY_ROW_DOWNLOAD_FAILED = {
  primary: "Download failed",
  support: "Download client reported a failed download",
} as const;

export const FETCHER_FI_POLICY_ROW_GENERIC_IMPORT = {
  primary: "Generic import error",
  support: "Import failed for a reason not covered above",
} as const;

/** One muted line per card under “Queue actions” (short selector labels need this context once). */
export const FETCHER_FI_POLICY_ACTION_LEGEND =
  "Remove deletes the queue item and asks the app to remove it from the download client. " +
  "Blocklist adds the release to the app blocklist. " +
  "Remove + blocklist does both.";

export const FETCHER_FI_POLICY_RUN_INTERVAL_LABEL = "Run interval (minutes)";
export const FETCHER_FI_POLICY_RUN_INTERVAL_HELPER =
  "How often Fetcher starts an automatic queue pass for this app. Use 0 minutes to turn timed passes off.";

export const FETCHER_FI_ACTION_LABEL_LEAVE_ALONE = "Leave alone";
export const FETCHER_FI_ACTION_LABEL_REMOVE_ONLY = "Remove";
export const FETCHER_FI_ACTION_LABEL_BLOCKLIST_ONLY = "Blocklist";
export const FETCHER_FI_ACTION_LABEL_REMOVE_AND_BLOCKLIST = "Remove + block";

export const FETCHER_FI_ACTION_OPTIONS: { value: FailedImportQueueHandlingAction; label: string }[] = [
  { value: "leave_alone", label: FETCHER_FI_ACTION_LABEL_LEAVE_ALONE },
  { value: "remove_only", label: FETCHER_FI_ACTION_LABEL_REMOVE_ONLY },
  { value: "blocklist_only", label: FETCHER_FI_ACTION_LABEL_BLOCKLIST_ONLY },
  { value: "remove_and_blocklist", label: FETCHER_FI_ACTION_LABEL_REMOVE_AND_BLOCKLIST },
];
