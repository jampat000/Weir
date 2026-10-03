/**
 * How the machine answers a load, one second at a time: the processor follows what Weir's tools are doing with some
 * other work wandering around it, memory drifts slowly, and the disk reads and writes follow the passes. Each reading
 * is smoothed from the last, so a pass starting or ending shows as a ramp rather than a step.
 */
import { HARDWARE, MEBIBYTE } from "./profiles.mjs";

const PERCENT = 100;
const SECONDS_PER_HOUR = 3600;

/** How quickly each reading follows its target: 1 follows at once, less lags behind. */
const FOLLOW = { tools: 0.55, io: 0.45, speed: 0.4, memoryFiles: 0.08 };
/** How hard the wandering readings are pulled back to their usual level each second. */
const PULL = { cpu: 0.08, memory: 0.02, backgroundIo: 0.06 };

const WEIR_IDLE_CPU_PERCENT = 0.5;
const WEIR_CPU_PERCENT_PER_RUNNING = 0.25;
const WEIR_CPU_PERCENT_PER_BROWSER = 0.3;
const WEIR_IDLE_MEMORY_BYTES = 210 * MEBIBYTE;
const WEIR_MEMORY_BYTES_PER_RUNNING = 55 * MEBIBYTE;
const MEMORY_SHARE_PER_RUNNING = 0.012;
const LEAST_MEMORY_SHARE = 0.05;
const MOST_MEMORY_SHARE = 0.97;
const IDLE_BUSY_PERCENT = 1.5;
const BUSY_WANDER_PERCENT = 0.5;
const TOOLS_JITTER = 0.06;

/**
 * One second of the machine's readings.
 * @typedef {object} Sample
 * @property {number} atMs
 * @property {number} cpuPercent
 * @property {number} cores
 * @property {number} memoryUsedBytes
 * @property {number} memoryTotalBytes
 * @property {number} diskReadBytesPerSecond
 * @property {number} diskWriteBytesPerSecond
 * @property {number} diskBusyPercent
 * @property {number} weirCpuPercent
 * @property {number} weirMemoryBytes
 * @property {number} toolsCpuPercent
 * @property {number} processingReadBytesPerSecond
 * @property {number} processingWriteBytesPerSecond
 * @property {number} processingSpeed
 * @property {number} running
 * @property {number} slots
 */

const follow = (previous, target, rate) =>
  previous + (target - previous) * rate;
const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

export class MachineModel {
  #profile;
  #rng;
  #otherCpu;
  #memoryShare;
  #memoryMean;
  #memoryFiles = 0;
  #backgroundRead;
  #backgroundWrite;
  #spike = { boost: 0, secondsLeft: 0, secondsTotal: 1 };
  #tools = 0;
  #read = 0;
  #write = 0;
  #speed = 0;

  /** @param {import("./profiles.mjs").Profile} profile @param {import("../engine/rng.mjs").Rng} rng */
  constructor(profile, rng) {
    this.#profile = profile;
    this.#rng = rng;
    this.#otherCpu = profile.idleCpuPercent;
    this.#memoryShare = profile.memoryShare;
    this.#memoryMean = profile.memoryShare;
    this.#backgroundRead = profile.backgroundIoBytesPerSecond;
    this.#backgroundWrite = profile.backgroundIoBytesPerSecond * 0.6;
  }

  /** A number that is usually near 0 and rarely beyond 1 either way. */
  #wander() {
    const spread = 0.5774;
    const sum =
      this.#rng.next() + this.#rng.next() + this.#rng.next() + this.#rng.next();
    return (sum - 2) / spread;
  }

  /** How much other work is bursting on the machine this second. */
  #spikeBoost() {
    const { spikes } = this.#profile;
    const spike = this.#spike;
    if (
      spike.secondsLeft <= 0 &&
      spikes &&
      this.#rng.chance(spikes.oddsPerSecond)
    ) {
      spike.boost = this.#rng.between(...spikes.boostPercent);
      spike.secondsTotal = Math.round(
        this.#rng.between(...spikes.lastsSeconds),
      );
      spike.secondsLeft = spike.secondsTotal;
    }
    if (spike.secondsLeft <= 0) return 0;
    spike.secondsLeft -= 1;
    return (
      spike.boost * Math.sqrt((spike.secondsLeft + 1) / spike.secondsTotal)
    );
  }

  #background(level, mean) {
    const { backgroundIoBytesPerSecond: base } = this.#profile;
    return Math.max(
      0,
      follow(level, mean, PULL.backgroundIo) + this.#wander() * base * 0.12,
    );
  }

  /**
   * The machine's readings after one more second under `load`.
   * @param {import("./load.mjs").Load} load
   * @param {number} atMs
   * @param {{ browsers?: number }} [options] How many browsers have the live stream open.
   * @returns {Sample}
   */
  step(load, atMs, { browsers = 0 } = {}) {
    const profile = this.#profile;
    const { running } = load;
    this.#otherCpu = Math.max(
      0.3,
      follow(this.#otherCpu, profile.idleCpuPercent, PULL.cpu) +
        this.#wander() * profile.idleCpuWander,
    );
    this.#tools = follow(this.#tools, load.toolsPercent, FOLLOW.tools);
    const tools = Math.max(
      0,
      this.#tools * (1 + this.#wander() * TOOLS_JITTER),
    );
    const weir =
      WEIR_IDLE_CPU_PERCENT +
      WEIR_CPU_PERCENT_PER_RUNNING * running +
      WEIR_CPU_PERCENT_PER_BROWSER * browsers +
      Math.abs(this.#wander()) * 0.15;
    const cpu = clamp(
      this.#otherCpu + weir + tools + this.#spikeBoost(),
      0,
      PERCENT,
    );

    this.#memoryMean += profile.memoryDriftPerHour / SECONDS_PER_HOUR;
    this.#memoryShare = clamp(
      follow(this.#memoryShare, this.#memoryMean, PULL.memory) +
        this.#wander() * profile.memoryWander,
      LEAST_MEMORY_SHARE,
      MOST_MEMORY_SHARE,
    );
    this.#memoryFiles = follow(this.#memoryFiles, running, FOLLOW.memoryFiles);
    const memoryShare = clamp(
      this.#memoryShare + this.#memoryFiles * MEMORY_SHARE_PER_RUNNING,
      LEAST_MEMORY_SHARE,
      MOST_MEMORY_SHARE,
    );

    this.#read = follow(this.#read, load.readBytesPerSecond, FOLLOW.io);
    this.#write = follow(this.#write, load.writeBytesPerSecond, FOLLOW.io);
    this.#speed = follow(this.#speed, load.speed, FOLLOW.speed);
    this.#backgroundRead = this.#background(
      this.#backgroundRead,
      profile.backgroundIoBytesPerSecond,
    );
    this.#backgroundWrite = this.#background(
      this.#backgroundWrite,
      profile.backgroundIoBytesPerSecond * 0.6,
    );
    const diskRead = this.#read + this.#backgroundRead;
    const diskWrite = this.#write + this.#backgroundWrite;

    return {
      atMs,
      cpuPercent: cpu,
      cores: HARDWARE.cores,
      memoryUsedBytes: Math.round(memoryShare * HARDWARE.memoryTotalBytes),
      memoryTotalBytes: HARDWARE.memoryTotalBytes,
      diskReadBytesPerSecond: Math.round(diskRead),
      diskWriteBytesPerSecond: Math.round(diskWrite),
      diskBusyPercent: clamp(
        IDLE_BUSY_PERCENT +
          Math.abs(this.#wander()) * BUSY_WANDER_PERCENT +
          ((diskRead + diskWrite) / HARDWARE.diskBytesPerSecond) * PERCENT,
        0,
        PERCENT,
      ),
      weirCpuPercent: weir,
      weirMemoryBytes: Math.round(
        WEIR_IDLE_MEMORY_BYTES + WEIR_MEMORY_BYTES_PER_RUNNING * running,
      ),
      toolsCpuPercent: tools,
      processingReadBytesPerSecond: Math.round(this.#read),
      processingWriteBytesPerSecond: Math.round(this.#write),
      processingSpeed: this.#speed < 0.5 ? 0 : this.#speed,
      running,
      slots: load.slots,
    };
  }
}
