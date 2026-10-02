/** A periodic task with a timer of its own: it waits out its interval, runs for a few seconds, and says how it went. */
import { toWire, SECOND_MS } from "../wire-time.mjs";

/** How long a task that may not run right now waits before it looks again, at normal speed. */
const BLOCKED_RETRY_MS = 5 * SECOND_MS;

/**
 * @typedef {object} TaskResult
 * @property {boolean} ok
 * @property {string | null} [error] A plain sentence saying why it failed.
 */

/**
 * What a task says about itself: the fields of a row of `GET /api/v1/system/tasks` other than its key and label, which
 * the registry adds.
 * @typedef {object} TaskStatus
 * @property {boolean} running
 * @property {string | null} last_run_at
 * @property {boolean | null} last_ok
 * @property {string | null} last_error
 * @property {string | null} next_run_at
 * @property {number | null} interval_seconds
 */

/** @param {number | null} ms */
export const wireOrNull = (ms) => (ms === null ? null : toWire(ms));

export class TimedTask {
  #speed;
  #runMs;
  #rng;
  #intervalSeconds;
  #retryMs;
  #canRun;
  #perform;
  #lastRunAt;
  #lastOk;
  #lastError;
  #nextRunAt;
  #runEndsAt = /** @type {number | null} */ (null);

  /**
   * @param {object} options
   * @param {number} options.speed How many times faster than normal the simulation runs.
   * @param {() => number} options.intervalSeconds The setting the task runs on, read each time so a change in Settings counts.
   * @param {[number, number]} options.runMs How long a run lasts, at normal speed.
   * @param {import("../engine/rng.mjs").Rng} options.rng
   * @param {{ at: number | null, ok: boolean | null, error?: string | null }} options.last How it last went before the session opened.
   * @param {number} options.nextRunAt
   * @param {(nowMs: number) => TaskResult} [options.perform] What the run does; it succeeds by default.
   * @param {() => boolean} [options.canRun] Whether it may start now.
   * @param {number} [options.retryMs] How long a failed task waits before it tries again, at normal speed; its interval by default.
   */
  constructor({
    speed,
    intervalSeconds,
    runMs,
    rng,
    last,
    nextRunAt,
    perform = () => ({ ok: true }),
    canRun = () => true,
    retryMs,
  }) {
    this.#speed = speed;
    this.#intervalSeconds = intervalSeconds;
    this.#runMs = runMs;
    this.#rng = rng;
    this.#lastRunAt = last.at;
    this.#lastOk = last.ok;
    this.#lastError = last.error ?? null;
    this.#nextRunAt = nextRunAt;
    this.#perform = perform;
    this.#canRun = canRun;
    this.#retryMs = retryMs;
  }

  /** @param {number} nowMs */
  advance(nowMs) {
    if (this.#runEndsAt !== null) {
      if (nowMs >= this.#runEndsAt) this.#finish(nowMs);
      return;
    }
    if (nowMs < this.#nextRunAt) return;
    if (!this.#canRun()) {
      this.#nextRunAt = nowMs + BLOCKED_RETRY_MS / this.#speed;
      return;
    }
    this.#runEndsAt = nowMs + this.#rng.between(...this.#runMs) / this.#speed;
  }

  #finish(nowMs) {
    const result = this.#perform(nowMs);
    this.#runEndsAt = null;
    this.#lastRunAt = nowMs;
    this.#lastOk = result.ok;
    this.#lastError = result.ok ? null : (result.error ?? null);
    const waitMs =
      !result.ok && this.#retryMs !== undefined
        ? this.#retryMs
        : this.#intervalSeconds() * SECOND_MS;
    this.#nextRunAt = nowMs + waitMs / this.#speed;
  }

  /** @returns {TaskStatus} */
  status() {
    return {
      running: this.#runEndsAt !== null,
      last_run_at: wireOrNull(this.#lastRunAt),
      last_ok: this.#lastOk,
      last_error: this.#lastError,
      next_run_at: toWire(this.#nextRunAt),
      interval_seconds: this.#intervalSeconds(),
    };
  }
}
