/** A workflow's library clean, as a row of the task list: the engine's own timer says when it is next due. */
import { JOB_KIND, JOB_STATUS } from "../engine/jobs.mjs";
import { wireOrNull } from "./timed-task.mjs";

export class CleanTask {
  #engine;
  #libraryId;
  #due = {
    nextRunAt: /** @type {number | null} */ (null),
    intervalSeconds: null,
  };

  /**
   * @param {object} options
   * @param {import("../engine/engine.mjs").Engine} options.engine
   * @param {number} options.libraryId
   */
  constructor({ engine, libraryId }) {
    this.#engine = engine;
    this.#libraryId = libraryId;
  }

  /** @param {number} nowMs */
  advance(nowMs) {
    this.#due = this.#engine.cleanDueTimes(nowMs).get(this.#libraryId) ?? {
      nextRunAt: null,
      intervalSeconds: null,
    };
  }

  #running() {
    return this.#engine.jobs
      .all()
      .some(
        (job) =>
          job.kind === JOB_KIND.LIBRARY_CLEAN &&
          job.status === JOB_STATUS.LEASED &&
          job.payload.library_id === this.#libraryId,
      );
  }

  /** @returns {import("./timed-task.mjs").TaskStatus} */
  status() {
    const last = this.#engine.cleanRecords.find(
      (record) => record.library_id === this.#libraryId,
    );
    return {
      running: this.#running(),
      last_run_at: last?.recorded_at ?? null,
      last_ok: last ? true : null,
      last_error: null,
      next_run_at: wireOrNull(this.#due.nextRunAt),
      interval_seconds: this.#due.intervalSeconds,
    };
  }
}
