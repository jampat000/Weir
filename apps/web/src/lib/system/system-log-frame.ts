import type { Schema } from "../api/types";

/** The name of the frame on the Activity stream. */
export const SYSTEM_LOG_EVENT = "system.log";

/** A warning or error that Weir just wrote to its log. */
export type SystemLogFrame = Schema<"SystemLogFrame">;

const LEVELS: readonly string[] = [
  "WARNING",
  "ERROR",
  "CRITICAL",
] satisfies SystemLogFrame["level"][];

/** The frame in a stream message, or null when it is not one this screen understands. */
export function parseSystemLogFrame(data: string): SystemLogFrame | null {
  try {
    const raw = JSON.parse(data) as Record<string, unknown>;
    if (
      typeof raw.at !== "string" ||
      typeof raw.level !== "string" ||
      !LEVELS.includes(raw.level) ||
      typeof raw.message !== "string"
    )
      return null;
    return {
      at: raw.at,
      level: raw.level as SystemLogFrame["level"],
      message: raw.message,
    };
  } catch {
    return null;
  }
}
