import { plural } from "../../../../lib/ui/mm-plural";

/** The levels the server log can be narrowed to; "" is every level. */
export type LogLevelFilter = "" | "INFO" | "WARNING" | "ERROR";

export const LOG_LEVEL_OPTIONS: { value: LogLevelFilter; label: string }[] = [
  { value: "", label: "All levels" },
  { value: "INFO", label: "Information" },
  { value: "WARNING", label: "Warnings" },
  { value: "ERROR", label: "Errors" },
];

/** How many entries one page of the server log shows: the most the server itself will ever return. */
export const SERVER_LOG_PAGE_SIZE = 250;

/** A level as the list shows it: INFO reads as a word, the others are already loud enough. */
export function logLevelLabel(level: string): string {
  return level === "INFO" ? "Information" : level;
}

/** Severity as a colour on the level word; the quiet body has no tinted cards to carry it. */
export function logLevelToneClass(level: string): string {
  switch (level.toUpperCase()) {
    case "ERROR":
    case "CRITICAL":
      return "mm-status-text--failed";
    case "WARNING":
      return "mm-status-text--warning";
    default:
      return "";
  }
}

const SECONDS_PER_DAY = 86_400;
const SECONDS_PER_HOUR = 3_600;

/** "2d 4h", "3h 12m", "7m"; "Just started" before the first second has passed. */
export function formatRuntimeUptime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return "Just started";
  const totalSeconds = Math.floor(seconds);
  const days = Math.floor(totalSeconds / SECONDS_PER_DAY);
  const hours = Math.floor((totalSeconds % SECONDS_PER_DAY) / SECONDS_PER_HOUR);
  const minutes = Math.floor((totalSeconds % SECONDS_PER_HOUR) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

/** Whole milliseconds from 100 up, one decimal below. */
export function formatAverageMs(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "0 ms";
  return `${value >= 100 ? value.toFixed(0) : value.toFixed(1)} ms`;
}

/**
 * The one number worth reading from the status counts: server failures first, then rejected or
 * missing requests. A browser asking for something that is not there is not an application failure.
 */
export function requestIssueSummary(
  statusCounts: Record<string, number> | undefined,
): { value: string; detail: string } {
  const counts = statusCounts ?? {};
  const success = counts["2xx"] ?? 0;
  const redirects = counts["3xx"] ?? 0;
  const rejectedOrMissing = counts["4xx"] ?? 0;
  const serverFailures = counts["5xx"] ?? 0;
  const detail = `Successful ${success} - Redirected ${redirects} - Rejected or not found ${rejectedOrMissing} - Server failures ${serverFailures}`;
  if (serverFailures > 0) {
    return {
      value: plural(serverFailures, "server failure", "server failures"),
      detail,
    };
  }
  if (rejectedOrMissing > 0) {
    return {
      value: plural(rejectedOrMissing, "request issue", "request issues"),
      detail,
    };
  }
  return { value: "No request issues", detail };
}
