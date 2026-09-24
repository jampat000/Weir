import type {
  MaintenanceFamily,
  MaintenanceFamilyState,
} from "../../../../lib/processing/maintenance-api";

const MINUTE = 60;
const HOUR = 3600;
const DAY = 86400;

/** The cleanup jobs Weir times, in words a person uses, with the setting each one's switch and timer save to. */
export const CLEANUP_JOBS: {
  family: MaintenanceFamily;
  name: string;
  enabledField:
    | "work_temp_stale_sweep_enabled"
    | "failure_cleanup_enabled"
    | "unclaimed_handback_cleanup_enabled";
  intervalField:
    | "work_temp_stale_sweep_interval_seconds"
    | "failure_cleanup_interval_seconds"
    | "unclaimed_handback_cleanup_interval_seconds";
  /** It deletes something a person cannot get back, so its description is painted in the warning colour. */
  destructive: boolean;
}[] = [
  {
    family: "work_temp_stale_sweep",
    name: "Leftover work files",
    enabledField: "work_temp_stale_sweep_enabled",
    intervalField: "work_temp_stale_sweep_interval_seconds",
    destructive: false,
  },
  {
    family: "failure_cleanup",
    name: "Downloads of failed files",
    enabledField: "failure_cleanup_enabled",
    intervalField: "failure_cleanup_interval_seconds",
    destructive: true,
  },
  {
    // #652: Weir's own cleaned copies that no media manager imported in time. Off until a person switches it on.
    family: "unclaimed_handbacks",
    name: "Cleaned copies nobody picked up",
    enabledField: "unclaimed_handback_cleanup_enabled",
    intervalField: "unclaimed_handback_cleanup_interval_seconds",
    destructive: true,
  },
];

export type CleanupJob = (typeof CLEANUP_JOBS)[number];

/** The intervals the "Every" list offers. */
export const EVERY: { seconds: number; label: string }[] = [
  { seconds: 15 * MINUTE, label: "15 minutes" },
  { seconds: 30 * MINUTE, label: "30 minutes" },
  { seconds: HOUR, label: "hour" },
  { seconds: 6 * HOUR, label: "6 hours" },
  { seconds: 12 * HOUR, label: "12 hours" },
  { seconds: DAY, label: "day" },
  { seconds: 7 * DAY, label: "7 days" },
];

/** "15 minutes", "hour", "3 days": the interval after the word "Every". */
export function everyWords(seconds: number): string {
  const known = EVERY.find((e) => e.seconds === seconds);
  if (known) return known.label;
  if (seconds % DAY === 0) return `${seconds / DAY} days`;
  if (seconds % HOUR === 0) return `${seconds / HOUR} hours`;
  return `${Math.round(seconds / MINUTE)} minutes`;
}

/** The list's choices, with the saved interval added when it is not one of them. */
export function everyChoices(interval: number): typeof EVERY {
  return EVERY.some((e) => e.seconds === interval)
    ? EVERY
    : [...EVERY, { seconds: interval, label: everyWords(interval) }];
}

/** "hour" reads "1 hour" on its own in the list. */
export function choiceLabel(label: string): string {
  return /^(hour|day)$/.test(label) ? `1 ${label}` : label;
}

export function lastRunLine(
  job: MaintenanceFamilyState,
  formatDate: (iso: string) => string,
): string {
  if (job.running > 0) return "Running now.";
  if (job.pending > 0) return "Queued, waiting for a free worker.";
  if (job.last_failed_at) {
    return `Failed ${formatDate(job.last_failed_at)}: ${job.last_error ?? "no reason recorded"}.`;
  }
  if (job.last_completed_at) return formatDate(job.last_completed_at);
  return "Not yet.";
}
