/**
 * How the simulation gives a row of System › Logs its level and its category. These are the server's rules
 * (SystemLogRules.cs), restated for the events, jobs and server lines the simulation has.
 */
import { JOB_KIND, JOB_STATUS } from "../engine/jobs.mjs";

export const SOURCE = Object.freeze({
  EVENT: "event",
  JOB: "job",
  SERVER: "server",
});

/** The order of rows that share a time: later source first, as the server pages them. */
export const SOURCE_ORDER = Object.freeze({
  [SOURCE.SERVER]: 0,
  [SOURCE.JOB]: 1,
  [SOURCE.EVENT]: 2,
});

export const LEVELS = Object.freeze(["error", "warning", "info", "success"]);

export const CATEGORIES = Object.freeze([
  "processing",
  "scans",
  "cleanup",
  "library",
  "connections",
  "backups",
  "sign_in",
  "updates",
  "weir",
]);

/** First prefix that matches wins; an event type none claims is about Weir itself. */
const EVENT_TYPE_CATEGORIES = [
  ["auth.", "sign_in"],
  ["arr_library.", "connections"],
  ["system.reconciliation.", "connections"],
  ["library.scan_", "scans"],
  ["library.file_change_notif", "connections"],
  ["library.", "library"],
  ["processing.handoff_", "connections"],
  ["processing.handback_outcome", "connections"],
  ["processing.downloaded_scan_", "connections"],
  ["processing.unclaimed_handback_cleanup", "cleanup"],
  ["processing.work_temp_stale_sweep", "cleanup"],
  ["processing.failure_cleanup_sweep", "cleanup"],
  ["processing.file_removal_", "cleanup"],
  ["processing.file_left_watched_folder", "scans"],
  ["processing.", "processing"],
];

const EVENT_RESULT_LEVELS = {
  failed: "error",
  warning: "warning",
  retrying: "warning",
  success: "success",
};

/** @param {string} eventType */
export function eventCategory(eventType) {
  return (
    EVENT_TYPE_CATEGORIES.find(([prefix]) =>
      eventType.startsWith(prefix),
    )?.[1] ?? "weir"
  );
}

/** @param {string | null} result */
export const eventLevel = (result) => EVENT_RESULT_LEVELS[result] ?? "info";

const JOB_KINDS = {
  [JOB_KIND.FILE_PASS]: { label: "Process a media file", category: "processing" },
  [JOB_KIND.LIBRARY_CLEAN]: { label: "Clean a library file", category: "library" },
};

/** @param {string} kind */
export const jobCategory = (kind) => JOB_KINDS[kind]?.category ?? "weir";

/** @param {string} kind */
export const jobKindLabel = (kind) => JOB_KINDS[kind]?.label ?? kind;

export const JOB_STATUS_LABELS = Object.freeze({
  [JOB_STATUS.PENDING]: "Queued",
  [JOB_STATUS.LEASED]: "Running",
  [JOB_STATUS.COMPLETED]: "Finished",
  [JOB_STATUS.FAILED]: "Failed",
  [JOB_STATUS.CANCELLED]: "Cancelled",
  handler_ok_finalize_failed: "Recovery needed",
});

/** @param {string} status @param {string | null} lastError */
export function jobLevel(status, lastError) {
  if (status === JOB_STATUS.PENDING && lastError) return "warning";
  if (status === JOB_STATUS.FAILED) return "error";
  if (status === "handler_ok_finalize_failed") return "warning";
  if (status === JOB_STATUS.COMPLETED) return "success";
  return "info";
}

/** @param {string} level The level as the log file words it. */
export function serverLevel(level) {
  const word = level.toUpperCase();
  if (word === "ERROR" || word === "CRITICAL") return "error";
  return word === "WARNING" ? "warning" : "info";
}

const SERVER_LOGGER_CATEGORIES = [
  [["backup"], "backups"],
  [["update"], "updates"],
  [["auth", "session", "rate_limit"], "sign_in"],
  [["cleanup", "sweep"], "cleanup"],
  [["scan", "watched_folder"], "scans"],
  [["connection", "media_manager", "handoff", "handback"], "connections"],
  [["library"], "library"],
  [["processing", "remux", "jobs", "worker"], "processing"],
];

/** @param {string} logger */
export function serverCategory(logger) {
  const name = logger.toLowerCase();
  return (
    SERVER_LOGGER_CATEGORIES.find(([fragments]) =>
      fragments.some((fragment) => name.includes(fragment)),
    )?.[1] ?? "weir"
  );
}
