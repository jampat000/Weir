/**
 * The computer the simulated Weir runs on, as System reads it: a reading every second kept for ten minutes, and the
 * drives behind the workflows. The readings follow what the engine is doing, and the ten minutes before the session
 * opened are filled in when the first reading is taken, so a trace is never empty.
 */
import { SECOND_MS, HOUR_MS } from "../wire-time.mjs";
import { DriveAlerts } from "./drive-alerts.mjs";
import { DriveLedger } from "./drives.mjs";
import { engineLoad, PastWork } from "./load.mjs";
import { MachineModel } from "./model.mjs";
import { profileFor } from "./profiles.mjs";

const RING_SECONDS = 600;
const INTERVAL_MS = SECOND_MS;
/** The machine had been up this long when the session opened. */
const UPTIME_AT_START_MS = 51 * HOUR_MS;

export class Machine {
  #engine;
  #profile;
  #model;
  #rng;
  #drives;
  #alerts;
  /** @type {import("./model.mjs").Sample[]} */
  #ring = [];
  #lastAt = /** @type {number | null} */ (null);
  #startedAt;
  #browsers = 0;
  #latestLoad = /** @type {import("./load.mjs").Load | null} */ (null);
  /** @type {Set<(sample: import("./model.mjs").Sample) => void>} */
  #listeners = new Set();

  /**
   * @param {object} options
   * @param {import("../engine/engine.mjs").Engine} options.engine
   * @param {import("../engine/rng.mjs").Rng} options.rng A source of its own, so the machine does not change which files turn up.
   * @param {number} options.startedAt
   */
  constructor({ engine, rng, startedAt }) {
    this.#engine = engine;
    this.#rng = rng;
    this.#startedAt = startedAt;
    this.#profile = profileFor(engine.scenario.name);
    this.#model = new MachineModel(this.#profile, rng);
    this.#drives = new DriveLedger({
      engine,
      facts: this.#profile.drives,
      startedAt,
    });
    this.#alerts = new DriveAlerts(engine);
  }

  get profile() {
    return this.#profile;
  }

  /** How many browsers have the live stream open. */
  get browsers() {
    return this.#browsers;
  }

  browserOpened() {
    this.#browsers += 1;
  }

  browserClosed() {
    this.#browsers = Math.max(0, this.#browsers - 1);
  }

  /** Calls `listener` with every reading as it is taken, once the session is under way. @param {(sample: import("./model.mjs").Sample) => void} listener */
  onSample(listener) {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  /** The newest reading, or null before the first. */
  get latest() {
    return this.#ring.at(-1) ?? null;
  }

  /** Every reading of the last ten minutes, oldest first. */
  history() {
    return this.#ring;
  }

  /** How long the machine has been up at `nowMs`, in ms. @param {number} nowMs */
  uptimeMs(nowMs) {
    return UPTIME_AT_START_MS + (nowMs - this.#startedAt);
  }

  /** The drives behind the workflows' folders at `nowMs`. @param {number} nowMs */
  drives(nowMs) {
    return this.#drives.read(nowMs, this.#latestLoad?.byDrive ?? new Map());
  }

  /** Takes every reading that has come due by `nowMs`. @param {number} nowMs */
  advance(nowMs) {
    const target = Math.floor(nowMs / INTERVAL_MS) * INTERVAL_MS;
    if (this.#lastAt === null) return this.#open(target);
    const behind = (target - this.#lastAt) / INTERVAL_MS;
    if (behind > RING_SECONDS)
      this.#lastAt = target - RING_SECONDS * INTERVAL_MS;
    if (this.#lastAt === target) return;
    while (this.#lastAt < target) {
      this.#lastAt += INTERVAL_MS;
      this.#take(engineLoad(this.#engine), this.#lastAt, true);
    }
    this.#alerts.check(this.drives(target), target);
  }

  /** Fills the ten minutes before `atMs` from the profile, then takes the first live reading. */
  #open(atMs) {
    const past = new PastWork(this.#profile.pastWork, this.#rng);
    const slots = this.#engine.slots();
    for (let ago = RING_SECONDS - 1; ago > 0; ago -= 1)
      this.#take(past.next(slots), atMs - ago * INTERVAL_MS, false);
    this.#lastAt = atMs;
    this.#take(engineLoad(this.#engine), atMs, false);
    this.drives(atMs);
  }

  #take(load, atMs, announce) {
    this.#latestLoad = load;
    const sample = this.#model.step(load, atMs, { browsers: this.#browsers });
    this.#ring.push(sample);
    if (this.#ring.length > RING_SECONDS) this.#ring.shift();
    if (announce) for (const listener of this.#listeners) listener(sample);
  }
}
