/**
 * The drives behind the workflows' folders: how full each is, how much of that is Weir's own files, how fast it is
 * being read and written, and when it will fill. Free space is what is left once everything else, and Weir's cleaned
 * copies that no manager has taken yet, have been counted, so it falls as files finish and recovers when a copy is
 * taken.
 */
import { SECOND_MS, DAY_MS, MINUTE_MS } from "../wire-time.mjs";
import { percentOf } from "../engine/pass.mjs";
import { STATUS } from "../engine/file.mjs";
import { VERDICT } from "../engine/plan.mjs";
import { DEFAULT_DRIVE, MEBIBYTE } from "./profiles.mjs";

const LETTER_DRIVE = /^([A-Za-z]):/;
const NETWORK_SHARE = /^\\\\([^\\]+)\\([^\\]+)/;
const FULL = 100;
/** Free space never reads below this, so a bar is never empty of room. */
const LEAST_FREE_BYTES = 0.4 * 1024 ** 3;
/** How long Weir watches free space fall before it says when the drive will be full. */
const MIN_OBSERVATION_MS = 2 * MINUTE_MS;
const TENTH = 10;
const IDLE_BUSY_PERCENT = 1.2;

/**
 * @typedef {object} DriveRef
 * @property {string} name "D:" or "\\\\nas\\media".
 * @property {string} path
 * @property {boolean} local Whether Weir can read how busy the drive is; a network share only has free space.
 */

/** The drive a folder is on, or null for a path that names none. @param {string} folder @returns {DriveRef | null} */
export function driveOf(folder) {
  const letter = LETTER_DRIVE.exec(folder)?.[1];
  if (letter)
    return {
      name: `${letter.toUpperCase()}:`,
      path: `${letter.toUpperCase()}:\\`,
      local: true,
    };
  const share = NETWORK_SHARE.exec(folder);
  if (!share) return null;
  const name = `\\\\${share[1]}\\${share[2]}`;
  return { name, path: name, local: false };
}

/** The folders of a workflow that a drive can hold, with the role each plays. @param {Record<string, any>} library */
export function foldersOf(library) {
  return [
    ["watched", library.watched_folder],
    ["work", library.work_folder],
    ["output", library.output_folder],
  ].filter(([, folder]) => typeof folder === "string" && folder !== "");
}

/**
 * Where a pass writes: its work folder, or the output folder when the workflow has none of its own.
 * @param {Record<string, any>} library
 */
export const writeDriveOf = (library) =>
  driveOf(library.work_folder || library.output_folder);

const copyBytes = (file) => file.plan.outputBytes ?? file.sizeBytes;

/** How much of a cleaned copy a pass has written so far; a pass that writes nothing has written nothing. @param {import("../engine/file.mjs").SimFile} file @param {number} nowMs */
function writtenSoFar(file, nowMs) {
  if (!file.run || file.plan.outputBytes === null) return 0;
  return (file.plan.outputBytes * (percentOf(file.run, nowMs) ?? 0)) / FULL;
}

/**
 * Weir's own files on a drive: the copies it has written and no manager has taken yet, and what a pass has written so
 * far.
 * @param {import("../engine/engine.mjs").Engine} engine
 * @param {string} driveName
 * @param {number} nowMs
 */
export function weirBytesOn(engine, driveName, nowMs) {
  let total = 0;
  for (const file of engine.files.values()) {
    const library = engine.library(file.libraryId);
    if (!library) continue;
    const waitingForManager =
      file.status === STATUS.PROCESSED &&
      file.plan.verdict === VERDICT.CLEAN &&
      file.handback !== null &&
      file.handback.outcome === null;
    if (waitingForManager && driveOf(library.output_folder)?.name === driveName)
      total += copyBytes(file);
    else if (
      file.status === STATUS.PROCESSING &&
      writeDriveOf(library)?.name === driveName
    )
      total += writtenSoFar(file, nowMs);
  }
  return total;
}

/**
 * What every drive reads as at one moment.
 * @typedef {object} DriveReading
 * @property {string} name
 * @property {string} path
 * @property {number} totalBytes
 * @property {number} freeBytes
 * @property {number} weirBytes
 * @property {number} keepFreeBytes
 * @property {number | null} fullInDays
 * @property {number | null} readBytesPerSecond
 * @property {number | null} writeBytesPerSecond
 * @property {number | null} busyPercent
 * @property {{ id: number, name: string, roles: string[] }[]} workflows
 */

export class DriveLedger {
  #engine;
  #facts;
  #startedAt;
  /** @type {Map<string, { otherUsedBytes: number, freeBytes: number, at: number }>} */
  #opening = new Map();

  /**
   * @param {object} options
   * @param {import("../engine/engine.mjs").Engine} options.engine
   * @param {Record<string, import("./profiles.mjs").DriveFacts>} options.facts
   * @param {number} options.startedAt
   */
  constructor({ engine, facts, startedAt }) {
    this.#engine = engine;
    this.#facts = facts;
    this.#startedAt = startedAt;
  }

  #factsOf(name) {
    return this.#facts[name] ?? DEFAULT_DRIVE;
  }

  /** The drives the workflows' folders are on, in the order the workflows name them. */
  #groups() {
    const groups = new Map();
    for (const library of this.#engine.store.libraries) {
      for (const [role, folder] of foldersOf(library)) {
        const drive = driveOf(folder);
        if (!drive) continue;
        const group = groups.get(drive.name) ?? { drive, workflows: new Map() };
        const roles = group.workflows.get(library.id) ?? {
          id: library.id,
          name: library.name,
          roles: [],
          keepFreeBytes: library.minimum_free_disk_space_mb * MEBIBYTE,
        };
        roles.roles.push(role);
        group.workflows.set(library.id, roles);
        groups.set(drive.name, group);
      }
    }
    return [...groups.values()];
  }

  /** What a drive held before Weir counted anything: set the first time it is read, so free space opens at the profile's figure. */
  #openingOf(name, weirBytes, nowMs) {
    let opening = this.#opening.get(name);
    if (!opening) {
      const facts = this.#factsOf(name);
      opening = {
        otherUsedBytes: facts.totalBytes - facts.freeBytes - weirBytes,
        freeBytes: facts.freeBytes,
        at: nowMs,
      };
      this.#opening.set(name, opening);
    }
    return opening;
  }

  /**
   * @param {number} nowMs
   * @param {Map<string, { readBytesPerSecond: number, writeBytesPerSecond: number }>} rates What Weir's passes are reading and writing on each drive.
   * @returns {DriveReading[]}
   */
  read(nowMs, rates) {
    return this.#groups().map(({ drive, workflows }) => {
      const facts = this.#factsOf(drive.name);
      const weirBytes = weirBytesOn(this.#engine, drive.name, nowMs);
      const opening = this.#openingOf(drive.name, weirBytes, nowMs);
      const otherUsed =
        opening.otherUsedBytes +
        (facts.otherGrowthBytesPerSecond * (nowMs - this.#startedAt)) /
          SECOND_MS;
      const freeBytes = Math.max(
        LEAST_FREE_BYTES,
        facts.totalBytes - otherUsed - weirBytes,
      );
      const io = rates.get(drive.name);
      const read = drive.local ? (io?.readBytesPerSecond ?? 0) : null;
      const write = drive.local ? (io?.writeBytesPerSecond ?? 0) : null;
      return {
        name: drive.name,
        path: drive.path,
        totalBytes: facts.totalBytes,
        freeBytes,
        weirBytes,
        keepFreeBytes: Math.max(
          ...[...workflows.values()].map((entry) => entry.keepFreeBytes),
        ),
        fullInDays: this.#fullInDays(opening, freeBytes, nowMs),
        readBytesPerSecond: read,
        writeBytesPerSecond: write,
        busyPercent: drive.local
          ? Math.min(
              FULL,
              IDLE_BUSY_PERCENT +
                (((read ?? 0) + (write ?? 0)) / facts.diskBytesPerSecond) *
                  FULL,
            )
          : null,
        workflows: [...workflows.values()].map(({ id, name, roles }) => ({
          id,
          name,
          roles,
        })),
      };
    });
  }

  /** Days until the drive is full at the pace it has been filling since the session opened, or null while it is not filling or has not been watched long enough. */
  #fullInDays(opening, freeBytes, nowMs) {
    const watchedMs = nowMs - opening.at;
    const filled = opening.freeBytes - freeBytes;
    if (watchedMs < MIN_OBSERVATION_MS || filled <= 0) return null;
    const bytesPerDay = (filled / watchedMs) * DAY_MS;
    return Math.round((freeBytes / bytesPerDay) * TENTH) / TENTH;
  }
}
