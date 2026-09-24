import type { ActivityRecentFilters } from "../api/activity-api";

/** The event log's filters as the form holds them: strings, with local datetime-local values. */
export type ActivityLogFilters = {
  eventType: string;
  search: string;
  from: string;
  to: string;
  trigger: string;
  result: string;
};

export const EMPTY_ACTIVITY_FILTERS: ActivityLogFilters = {
  eventType: "",
  search: "",
  from: "",
  to: "",
  trigger: "",
  result: "",
};

/** A local date and time in the shape a datetime-local input holds. */
function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Yesterday 18:00 to today 08:00, local time: when overnight scheduled work runs. */
export function lastNightRange(now: Date): { from: string; to: string } {
  const from = new Date(now);
  from.setDate(from.getDate() - 1);
  from.setHours(18, 0, 0, 0);
  const to = new Date(now);
  to.setHours(8, 0, 0, 0);
  return { from: toLocalInput(from), to: toLocalInput(to) };
}

export function last24HoursRange(now: Date): { from: string; to: string } {
  return {
    from: toLocalInput(new Date(now.getTime() - 24 * 60 * 60 * 1000)),
    to: "",
  };
}

function localInputToIso(value: string): string | undefined {
  if (!value.trim()) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? undefined : parsed.toISOString();
}

/**
 * What the server is asked for. `about: "weir"` keeps Weir's own events; a file's story is on History,
 * so the log never shows an entry about one file.
 */
export function activityLogQuery(
  applied: ActivityLogFilters,
): ActivityRecentFilters & { limit: number } {
  return {
    limit: 100,
    about: "weir",
    event_type: applied.eventType || undefined,
    search: applied.search.trim() || undefined,
    date_from: localInputToIso(applied.from),
    date_to: localInputToIso(applied.to),
    trigger: applied.trigger || undefined,
    result: applied.result || undefined,
  };
}

export function anyFilterSet(applied: ActivityLogFilters): boolean {
  return Boolean(
    applied.eventType ||
    applied.search.trim() ||
    applied.from ||
    applied.to ||
    applied.trigger ||
    applied.result,
  );
}

/** How many of the filters behind "More filters" are set, so a closed panel still says so. */
export function extraFilterCount(applied: ActivityLogFilters): number {
  return [
    applied.eventType,
    applied.result,
    applied.trigger,
    applied.from,
    applied.to,
  ].filter(Boolean).length;
}
