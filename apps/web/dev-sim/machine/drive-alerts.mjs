/** Says in the Activity record, now and then, that a drive has less free space than the workflows on it ask Weir to keep. */
import { SECOND_MS } from "../wire-time.mjs";
import { GIBIBYTE } from "./profiles.mjs";

export const DISK_SPACE_LOW = "system.disk_space_low";
/** How long Weir waits before saying it again about the same drive, at normal speed. */
const REPEAT_MS = 90 * SECOND_MS;
const TENTH = 10;

/** Whole tenths of a gigabyte, rounded down so a drive just under its limit never reads as at it. @param {number} bytes */
const gigabytes = (bytes) =>
  `${Math.floor((bytes / GIBIBYTE) * TENTH) / TENTH} GB`;

export class DriveAlerts {
  #engine;
  /** @type {Map<string, number>} When each drive was last reported, by name. */
  #reportedAt = new Map();

  /** @param {import("../engine/engine.mjs").Engine} engine */
  constructor(engine) {
    this.#engine = engine;
  }

  /**
   * @param {import("./drives.mjs").DriveReading[]} drives
   * @param {number} nowMs
   */
  check(drives, nowMs) {
    for (const drive of drives) {
      if (drive.freeBytes >= drive.keepFreeBytes) {
        this.#reportedAt.delete(drive.name);
        continue;
      }
      const last = this.#reportedAt.get(drive.name);
      if (last !== undefined && nowMs - last < REPEAT_MS / this.#engine.speed)
        continue;
      this.#reportedAt.set(drive.name, nowMs);
      this.#engine.activity.record(
        {
          type: DISK_SPACE_LOW,
          title: `Free space on ${drive.name} is ${gigabytes(drive.freeBytes)}, below the ${gigabytes(drive.keepFreeBytes)} Weir keeps free`,
          result: "warning",
          trigger: "scheduled",
          detail: {
            drive: drive.name,
            free_bytes: Math.round(drive.freeBytes),
            keep_free_bytes: drive.keepFreeBytes,
          },
        },
        nowMs,
      );
      this.#engine.touch();
    }
  }
}
