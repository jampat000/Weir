/**
 * What Weir is asking of the machine at one moment, in the terms the machine reacts to: how fast its passes read and
 * write, how fast ffmpeg is going, and how much of the processor its tools use. The live load is read off the engine;
 * the load before the session opened is made up from the scenario's profile.
 */
import { JOB_KIND, JOB_STATUS } from "../engine/jobs.mjs";
import { currentStage, STAGE } from "../engine/pass.mjs";
import { STATUS } from "../engine/file.mjs";
import { driveOf, writeDriveOf } from "./drives.mjs";
import { SECOND_MS } from "../wire-time.mjs";
import { MEBIBYTE } from "./profiles.mjs";

/** The share of the whole machine's processor one tool uses in each step of a pass. */
const TOOLS_PERCENT_BY_STAGE = {
  [STAGE.CHECKING]: 2.5,
  [STAGE.PLANNING]: 2,
  [STAGE.WRITING]: 7,
  [STAGE.VERIFYING]: 3.5,
  [STAGE.HANDING_BACK]: 1.5,
};
const TOOLS_PERCENT_FOR_CLEAN = 4;
const PROBE_READ_BYTES_PER_SECOND = 8 * MEBIBYTE;
const CLEAN_BYTES_PER_SECOND = 90 * MEBIBYTE;

/**
 * @typedef {object} Load
 * @property {number} readBytesPerSecond
 * @property {number} writeBytesPerSecond
 * @property {number} speed The speeds of the passes that are writing, added together, each as a multiple of playback; 0 when nothing is writing.
 * @property {number} toolsPercent The share of the whole machine's processor ffmpeg and mkvmerge use.
 * @property {number} running
 * @property {number} slots
 * @property {Map<string, { readBytesPerSecond: number, writeBytesPerSecond: number }>} byDrive
 */

/** The load of a machine Weir is not working. @param {number} slots @returns {Load} */
const idleLoad = (slots) => ({
  readBytesPerSecond: 0,
  writeBytesPerSecond: 0,
  speed: 0,
  toolsPercent: 0,
  running: 0,
  slots,
  byDrive: new Map(),
});

const NO_IO = () => ({ readBytesPerSecond: 0, writeBytesPerSecond: 0 });

function addToDrive(byDrive, drive, readBytesPerSecond, writeBytesPerSecond) {
  if (!drive) return;
  const io = byDrive.get(drive.name) ?? NO_IO();
  io.readBytesPerSecond += readBytesPerSecond;
  io.writeBytesPerSecond += writeBytesPerSecond;
  byDrive.set(drive.name, io);
}

/**
 * The load the engine's running passes and library cleans put on the machine right now.
 * @param {import("../engine/engine.mjs").Engine} engine
 * @returns {Load}
 */
export function engineLoad(engine) {
  const load = {
    readBytesPerSecond: 0,
    writeBytesPerSecond: 0,
    speed: 0,
    toolsPercent: 0,
    running: engine.running(),
    slots: engine.slots(),
    byDrive: new Map(),
  };
  for (const file of engine.files.values()) {
    if (file.status !== STATUS.PROCESSING || !file.run) continue;
    const library = engine.library(file.libraryId);
    const stage = currentStage(file.run);
    load.toolsPercent += TOOLS_PERCENT_BY_STAGE[stage];
    let read = 0;
    let write = 0;
    if (stage === STAGE.WRITING) {
      const seconds = file.run.timeline[file.run.index].ms / SECOND_MS;
      read = file.sizeBytes / seconds;
      write = (file.plan.outputBytes ?? file.sizeBytes) / seconds;
      load.speed += file.run.speedTimes;
    } else if (stage === STAGE.CHECKING) {
      read = PROBE_READ_BYTES_PER_SECOND;
    }
    load.readBytesPerSecond += read;
    load.writeBytesPerSecond += write;
    if (library) {
      addToDrive(load.byDrive, driveOf(library.watched_folder), read, 0);
      addToDrive(load.byDrive, writeDriveOf(library), 0, write);
    }
  }
  for (const job of engine.jobs.all()) {
    if (job.kind !== JOB_KIND.LIBRARY_CLEAN || job.status !== JOB_STATUS.LEASED)
      continue;
    load.toolsPercent += TOOLS_PERCENT_FOR_CLEAN;
    load.readBytesPerSecond += CLEAN_BYTES_PER_SECOND;
    load.writeBytesPerSecond += CLEAN_BYTES_PER_SECOND;
    const drive = driveOf(
      engine.library(job.payload.library_id)?.output_folder ?? "",
    );
    addToDrive(
      load.byDrive,
      drive,
      CLEAN_BYTES_PER_SECOND,
      CLEAN_BYTES_PER_SECOND,
    );
  }
  return load;
}

/** The passes Weir was running in the ten minutes before the session opened, made up second by second from a profile. */
export class PastWork {
  #plan;
  #rng;
  /** @type {{ secondsLeft: number, writeBytesPerSecond: number, speed: number }[]} */
  #passes = [];
  #slotFreeIn = 0;

  /** @param {import("./profiles.mjs").PastWork | null} plan @param {import("../engine/rng.mjs").Rng} rng */
  constructor(plan, rng) {
    this.#plan = plan;
    this.#rng = rng;
  }

  /** The load for the next second of the past. @param {number} slots @returns {Load} */
  next(slots) {
    const plan = this.#plan;
    if (!plan) return idleLoad(slots);
    for (const pass of this.#passes) pass.secondsLeft -= 1;
    this.#passes = this.#passes.filter((pass) => pass.secondsLeft > 0);
    this.#slotFreeIn -= 1;
    if (this.#passes.length < plan.concurrency && this.#slotFreeIn <= 0) {
      this.#passes.push({
        secondsLeft: this.#rng.between(...plan.passSeconds),
        writeBytesPerSecond: this.#rng.between(...plan.writeBytesPerSecond),
        speed: this.#rng.between(...plan.speed),
      });
      this.#slotFreeIn = this.#rng.between(...plan.gapSeconds);
    }
    const write = this.#passes.reduce(
      (sum, pass) => sum + pass.writeBytesPerSecond,
      0,
    );
    return {
      byDrive: new Map(),
      running: this.#passes.length,
      slots,
      readBytesPerSecond: write * plan.readOverWrite,
      writeBytesPerSecond: write,
      speed: this.#passes.reduce((sum, pass) => sum + pass.speed, 0),
      toolsPercent: this.#passes.length * TOOLS_PERCENT_BY_STAGE[STAGE.WRITING],
    };
  }
}
