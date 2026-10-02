/** What the Backups and tools card says: the backup schedule and the newest backups, the tools, and where updating stands. */
import { formatBytes } from "../../../../lib/format/bytes";
import type {
  ConfigurationBackupItem,
  UpdateStateOut,
  UpdateStatus,
} from "../../../../lib/settings/types";
import { parseAppTime } from "../../../../lib/ui/mm-format-date";
import type { MmStatusTone } from "../../../../lib/ui/mm-status-tone";
import { spanWords } from "./system-time";

/** How many backups the card lists. */
export const BACKUPS_LISTED = 3;
const HOURS_PER_DAY = 24;

export type BackupRow = {
  id: number;
  /** When it was made, in ms since the epoch. */
  at: number;
  size: string;
};

/** The newest backups, newest first. A backup whose time cannot be read is left out. */
export function latestBackups(
  items: readonly ConfigurationBackupItem[],
  count = BACKUPS_LISTED,
): BackupRow[] {
  return items
    .flatMap((item) => {
      const at = parseAppTime(item.created_at);
      return at === null
        ? []
        : [{ id: item.id, at, size: formatBytes(item.size_bytes) }];
    })
    .sort((a, b) => b.at - a.at)
    .slice(0, count);
}

/** "Every day", "Every 2 days", "Every 12 hours": how often the backup may run. */
function intervalWords(hours: number): string {
  if (hours === HOURS_PER_DAY) return "Every day";
  if (hours % HOURS_PER_DAY === 0) return `Every ${hours / HOURS_PER_DAY} days`;
  return `Every ${hours} hours`;
}

/** "03:00" as "3:00 am". A time that does not read is shown as it is. */
export function timeOfDayWords(value: string): string {
  const match = /^(\d{1,2}):(\d{2})/.exec(value);
  if (!match) return value;
  const time = new Date(2000, 0, 1, Number(match[1]), Number(match[2]));
  return new Intl.DateTimeFormat(undefined, {
    hour: "numeric",
    minute: "2-digit",
  })
    .format(time)
    .toLowerCase();
}

export type BackupSchedule = {
  enabled: boolean;
  intervalHours: number;
  preferredTime: string;
};

/** The schedule in a line: "Every day at 3:00 am", or that automatic backups are off. */
export function scheduleWords(schedule: BackupSchedule): string {
  if (!schedule.enabled) return "Automatic backups are off";
  return `${intervalWords(schedule.intervalHours)} at ${timeOfDayWords(schedule.preferredTime)}`;
}

/** "next in 5 h" for the time the next backup runs; "due now" once it has come; empty when there is none to say. */
export function nextBackupWords(nextAt: number | null, now: number): string {
  if (nextAt === null) return "";
  return nextAt <= now ? "due now" : `next in ${spanWords(nextAt - now)}`;
}

export type UpdateFacts = {
  current: string;
  /** The newest version, or "—" when Weir could not look. */
  latest: string;
  state: string;
  tone: MmStatusTone;
};

const NO_VERSION = "—";

/** Where updating stands: the version running, the newest, and one word for what happens next. */
export function updateFacts(
  status: UpdateStatus,
  state: UpdateStateOut | undefined,
): UpdateFacts {
  const base = {
    current: status.current_version,
    latest: status.latest_version ?? NO_VERSION,
  };
  if (status.status === "update_available") {
    return state?.downloaded
      ? { ...base, state: "Downloaded and ready", tone: "info" }
      : { ...base, state: "Update available", tone: "warning" };
  }
  if (status.status === "up_to_date") {
    return { ...base, state: "Up to date", tone: "healthy" };
  }
  return { ...base, state: "Could not check", tone: "neutral" };
}
