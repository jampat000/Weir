/**
 * The computer the simulated Weir runs on, and how each scenario treats it: how much else the machine is doing, how
 * its memory moves, where its drives stand, and what it was doing in the ten minutes before the session opened.
 */
import { SCENARIO } from "../scenarios.mjs";
import { TASK_KEY } from "../tasks/weir-tasks.mjs";

export const MEBIBYTE = 1024 ** 2;
export const GIBIBYTE = 1024 ** 3;
const TEBIBYTE = 1024 ** 4;

export const HARDWARE = Object.freeze({
  os: "Windows 11 Pro",
  cores: 16,
  memoryTotalBytes: 32 * GIBIBYTE,
  /** What the system drive can move in a second, reading and writing together. */
  diskBytesPerSecond: 2.4 * GIBIBYTE,
});

/**
 * What a drive starts the session with, by name. A drive no profile names is read as {@link DEFAULT_DRIVE}.
 * @typedef {object} DriveFacts
 * @property {number} totalBytes
 * @property {number} freeBytes How much is free when the session opens, counting what Weir has written.
 * @property {number} otherGrowthBytesPerSecond How fast everything that is not Weir fills it.
 * @property {number} diskBytesPerSecond What the drive can move in a second, reading and writing together.
 */

/** @type {DriveFacts} */
export const DEFAULT_DRIVE = {
  totalBytes: 2 * TEBIBYTE,
  freeBytes: 0.9 * TEBIBYTE,
  otherGrowthBytesPerSecond: MEBIBYTE,
  diskBytesPerSecond: 0.5 * GIBIBYTE,
};

/**
 * @typedef {object} PastWork
 * @property {number} concurrency How many passes the machine usually has writing at once.
 * @property {[number, number]} passSeconds
 * @property {[number, number]} gapSeconds How long a free slot waits before the next pass starts.
 * @property {[number, number]} writeBytesPerSecond
 * @property {number} readOverWrite How much faster a pass reads its source than it writes the cleaned copy.
 * @property {[number, number]} speed ffmpeg's speed, as a multiple of playback.
 */

/**
 * @typedef {object} Profile
 * @property {number} idleCpuPercent What everything else on the machine uses when Weir is idle.
 * @property {number} idleCpuWander How far that wanders from second to second.
 * @property {{ oddsPerSecond: number, boostPercent: [number, number], lastsSeconds: [number, number] } | null} spikes Bursts of other work.
 * @property {number} memoryShare The share of memory in use when the session opens.
 * @property {number} memoryDriftPerHour How much that share rises in an hour.
 * @property {number} memoryWander How far it wanders from second to second.
 * @property {number} backgroundIoBytesPerSecond What the rest of the machine reads and writes.
 * @property {PastWork | null} pastWork What Weir was doing before the session opened.
 * @property {Record<string, DriveFacts>} drives
 * @property {boolean} rebootPending
 * @property {number} errorsToday Requests that failed.
 * @property {number} restartsThisWeek
 * @property {{ taskKey: string, error: string, failedMinutesAgo: number, retryMinutes: number } | null} failingTask A scheduled task that is failing.
 */

const BUSY_PAST_WORK = {
  concurrency: 2,
  passSeconds: [12, 38],
  gapSeconds: [0, 6],
  writeBytesPerSecond: [150 * MEBIBYTE, 340 * MEBIBYTE],
  readOverWrite: 1.15,
  speed: [220, 980],
};

const SYSTEM_DRIVE = {
  totalBytes: 4 * TEBIBYTE,
  freeBytes: 1.5 * TEBIBYTE,
  otherGrowthBytesPerSecond: 1.8 * MEBIBYTE,
  diskBytesPerSecond: HARDWARE.diskBytesPerSecond,
};

const BACKUP_DRIVE = {
  ...DEFAULT_DRIVE,
  freeBytes: 0.9 * TEBIBYTE,
  otherGrowthBytesPerSecond: 0.3 * MEBIBYTE,
};

/** @type {Record<string, Profile>} */
const PROFILES = {
  [SCENARIO.BUSY]: {
    idleCpuPercent: 5,
    idleCpuWander: 0.9,
    spikes: null,
    memoryShare: 0.43,
    memoryDriftPerHour: 0.004,
    memoryWander: 0.0006,
    backgroundIoBytesPerSecond: 4 * MEBIBYTE,
    pastWork: BUSY_PAST_WORK,
    drives: { "D:": SYSTEM_DRIVE, "E:": BACKUP_DRIVE },
    rebootPending: false,
    errorsToday: 0,
    restartsThisWeek: 0,
    failingTask: null,
  },
  [SCENARIO.QUIET]: {
    idleCpuPercent: 2.5,
    idleCpuWander: 0.12,
    spikes: null,
    memoryShare: 0.27,
    memoryDriftPerHour: 0,
    memoryWander: 0.00006,
    backgroundIoBytesPerSecond: 0.4 * MEBIBYTE,
    pastWork: null,
    drives: { "D:": SYSTEM_DRIVE, "E:": BACKUP_DRIVE },
    rebootPending: false,
    errorsToday: 0,
    restartsThisWeek: 0,
    failingTask: null,
  },
  [SCENARIO.TROUBLE]: {
    idleCpuPercent: 11,
    idleCpuWander: 2.4,
    spikes: {
      oddsPerSecond: 1 / 40,
      boostPercent: [32, 62],
      lastsSeconds: [3, 9],
    },
    memoryShare: 0.79,
    memoryDriftPerHour: 0.07,
    memoryWander: 0.0014,
    backgroundIoBytesPerSecond: 14 * MEBIBYTE,
    pastWork: { ...BUSY_PAST_WORK, concurrency: 3 },
    drives: {
      "D:": { ...SYSTEM_DRIVE, freeBytes: 18.6 * GIBIBYTE },
      "E:": BACKUP_DRIVE,
    },
    rebootPending: true,
    errorsToday: 7,
    restartsThisWeek: 2,
    failingTask: {
      taskKey: TASK_KEY.BACKUP,
      error:
        "Weir could not write the backup to E:\\Backups\\Weir. Check that the drive is connected and has room.",
      failedMinutesAgo: 14,
      retryMinutes: 5,
    },
  },
};

/** @param {string} scenarioName */
export const profileFor = (scenarioName) =>
  PROFILES[scenarioName] ?? PROFILES[SCENARIO.BUSY];
