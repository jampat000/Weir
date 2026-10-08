import type { SystemOverview } from "./system-stats-types";

/** The name of the frame on the Activity stream. */
export const SYSTEM_OVERVIEW_EVENT = "system.overview";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

const isNumber = (value: unknown): value is number =>
  typeof value === "number" && Number.isFinite(value);

const isText = (value: unknown): value is string => typeof value === "string";

function hasNumbers(value: unknown, keys: readonly string[]): boolean {
  return isRecord(value) && keys.every((key) => isNumber(value[key]));
}

function isOverview(value: unknown): value is SystemOverview {
  return (
    isRecord(value) &&
    isText(value.version) &&
    isText(value.started_at) &&
    isText(value.runs_as) &&
    isText(value.address) &&
    isNumber(value.uptime_seconds) &&
    isNumber(value.data_bytes) &&
    isNumber(value.browsers_live) &&
    isNumber(value.restarts_this_week) &&
    isRecord(value.update) &&
    isText(value.update.status) &&
    hasNumbers(value.requests, ["median_ms", "p95_ms", "errors_today"]) &&
    hasNumbers(value.jobs_today, ["run", "failed"]) &&
    hasNumbers(value.checks, ["passing", "total"])
  );
}

/** The overview in a stream message, or null when it is not one this screen understands. */
export function parseSystemOverviewFrame(data: string): SystemOverview | null {
  try {
    const raw: unknown = JSON.parse(data);
    return isOverview(raw) ? raw : null;
  } catch {
    return null;
  }
}
