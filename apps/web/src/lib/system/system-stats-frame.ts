import type {
  SystemNow,
  SystemPoint,
  SystemStatsFrame,
} from "./system-stats-types";

/** The name of the frame on the Activity stream. */
export const SYSTEM_STATS_EVENT = "system.stats";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

const isNumber = (value: unknown): value is number =>
  typeof value === "number" && Number.isFinite(value);

const isNumberOrNull = (value: unknown): boolean =>
  value === null || isNumber(value);

function isNow(value: unknown): value is SystemNow {
  return (
    isRecord(value) &&
    typeof value.at === "string" &&
    isNumberOrNull(value.cpu_percent) &&
    isNumber(value.cores) &&
    isNumberOrNull(value.memory_used_bytes) &&
    isNumberOrNull(value.memory_total_bytes) &&
    isNumber(value.running) &&
    isNumber(value.slots)
  );
}

function isPoint(value: unknown): value is SystemPoint {
  return (
    isRecord(value) &&
    typeof value.at === "string" &&
    isNumberOrNull(value.cpu_percent) &&
    isNumberOrNull(value.memory_percent) &&
    isNumberOrNull(value.disk_read_bytes_per_sec) &&
    isNumberOrNull(value.disk_write_bytes_per_sec)
  );
}

/** The frame in a stream message, or null when it is not one this screen understands. */
export function parseSystemStatsFrame(data: string): SystemStatsFrame | null {
  try {
    const raw: unknown = JSON.parse(data);
    if (!isRecord(raw) || !isNow(raw.now) || !isPoint(raw.point)) return null;
    return { now: raw.now, point: raw.point };
  } catch {
    return null;
  }
}
