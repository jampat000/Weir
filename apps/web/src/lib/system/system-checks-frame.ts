import { parseAppTime } from "../ui/mm-format-date";

/** The name of the frame on the Activity stream. */
export const SYSTEM_CHECKS_EVENT = "system.checks";

/** When the server last looked, on its own, at what Health shows, in ms since the epoch. Null before its first look. */
export type ServerLooks = {
  foldersCheckedAt: number | null;
  readinessCheckedAt: number | null;
};

export const NO_LOOKS_YET: ServerLooks = {
  foldersCheckedAt: null,
  readinessCheckedAt: null,
};

function timeOf(value: unknown): number | null {
  return typeof value === "string" ? parseAppTime(value) : null;
}

/** The times in a stream message, or null when it is not one this screen understands. */
export function parseSystemChecksFrame(data: string): ServerLooks | null {
  try {
    const raw: unknown = JSON.parse(data);
    if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
      return null;
    }
    const frame = raw as Record<string, unknown>;
    return {
      foldersCheckedAt: timeOf(frame.folders_checked_at),
      readinessCheckedAt: timeOf(frame.readiness_checked_at),
    };
  } catch {
    return null;
  }
}
