import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import type { AppSettings } from "../../../../lib/settings/types";
import { setupTabPath } from "../../../../lib/settings/setup-areas";
import { CLEANUP_JOBS, everyWords } from "../cleanup/cleanup-jobs";
import { backupWords } from "../schedule/schedule-model";

const DEFAULT_BACKUP_HOURS = 24;
const DEFAULT_BACKUP_TIME = "02:00";
const SECONDS_PER_HOUR = 3600;

/** One of Weir's own jobs as a row: its name, when it runs and where that is changed. */
export type TimerRow = {
  key: string;
  name: string;
  /** When it runs, in words: "Every hour", "Off". */
  when: string;
  /** How often it runs, for sorting; null while it is off or unknown. */
  everySeconds: number | null;
  /** ISO times as the server gave them; null when there is none to show. */
  lastRun: string | null;
  nextRun: string | null;
  change: { to: string; label: string; ariaLabel: string };
};

function cleanupWhen(state: MaintenanceFamilyState | undefined): {
  when: string;
  everySeconds: number | null;
} {
  if (!state) return { when: "—", everySeconds: null };
  if (!state.enabled) return { when: "Off", everySeconds: null };
  return state.interval_seconds
    ? {
        when: `Every ${everyWords(state.interval_seconds)}`,
        everySeconds: state.interval_seconds,
      }
    : { when: "On", everySeconds: null };
}

function backupRow(settings: AppSettings): TimerRow {
  const enabled = Boolean(settings.configuration_backup_enabled);
  const hours = Number(
    settings.configuration_backup_interval_hours ?? DEFAULT_BACKUP_HOURS,
  );
  return {
    key: "settings-backup",
    name: "Settings backup",
    when: backupWords(
      enabled,
      hours,
      settings.configuration_backup_preferred_time ?? DEFAULT_BACKUP_TIME,
    ),
    everySeconds: enabled
      ? (hours > 0 ? hours : DEFAULT_BACKUP_HOURS) * SECONDS_PER_HOUR
      : null,
    lastRun: settings.configuration_backup_last_run_at ?? null,
    nextRun: null,
    change: {
      to: "/system?tab=backups",
      label: "Backups",
      ariaLabel: "Change in Backups",
    },
  };
}

/** The cleanup jobs, then the settings backup: each on its own clock, whatever the workflow hours say. */
export function timerRows(
  families: readonly MaintenanceFamilyState[],
  settings: AppSettings,
): TimerRow[] {
  const cleanup = CLEANUP_JOBS.map((job): TimerRow => {
    const state = families.find((family) => family.family === job.family);
    return {
      key: job.family,
      name: job.name,
      ...cleanupWhen(state),
      lastRun: state?.last_completed_at ?? null,
      nextRun: state?.enabled ? (state.next_run_at ?? null) : null,
      change: {
        to: setupTabPath("cleanup"),
        label: "Cleanup",
        ariaLabel: "Change in Cleanup",
      },
    };
  });
  return [...cleanup, backupRow(settings)];
}
