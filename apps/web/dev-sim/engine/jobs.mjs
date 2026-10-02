/**
 * The job queue behind the work: one job per file pass and per library clean, as System › Jobs and the Processing
 * board's library-clean cards read it. Job kinds are the server's.
 */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, MINUTE_MS } from "../wire-time.mjs";

export const JOB_KIND = Object.freeze({
  FILE_PASS: "processing.file.remux_pass.v1",
  LIBRARY_CLEAN: "processing.library.clean.v1",
});

export const JOB_STATUS = Object.freeze({
  PENDING: "pending",
  LEASED: "leased",
  COMPLETED: "completed",
  FAILED: "failed",
  CANCELLED: "cancelled",
});

const WORKER_NAME = "weir-worker";
const LEASE_MS = 5 * MINUTE_MS;
const MAX_ATTEMPTS = 3;
/** The newest jobs kept; older ones have been trimmed from a real queue long since. */
const KEPT_JOBS = 400;

/**
 * @typedef {object} Job
 * @property {number} id
 * @property {string} kind
 * @property {string} status
 * @property {Record<string, unknown>} payload
 * @property {number} createdAt
 * @property {number} updatedAt
 * @property {string | null} lastError
 */

export class JobBook {
  /** @type {Map<number, Job>} */
  #jobs = new Map();
  #nextId = 500;

  /**
   * @param {string} kind
   * @param {Record<string, unknown>} payload
   * @param {number} nowMs
   * @returns {Job}
   */
  create(kind, payload, nowMs) {
    const job = {
      id: this.#nextId++,
      kind,
      status: JOB_STATUS.PENDING,
      payload,
      createdAt: nowMs,
      updatedAt: nowMs,
      lastError: null,
    };
    this.#jobs.set(job.id, job);
    if (this.#jobs.size > KEPT_JOBS)
      this.#jobs.delete(this.#jobs.keys().next().value);
    return job;
  }

  /** Takes a job off the queue, as when the file it was for turns out not to need a pass. @param {number} id */
  remove(id) {
    this.#jobs.delete(id);
  }

  /** @param {number} id */
  get(id) {
    return this.#jobs.get(id) ?? null;
  }

  /**
   * @param {number} id
   * @param {string} status
   * @param {number} nowMs
   * @param {string | null} [lastError]
   */
  setStatus(id, status, nowMs, lastError = null) {
    const job = this.#jobs.get(id);
    if (!job) return;
    job.status = status;
    job.updatedAt = nowMs;
    job.lastError = lastError;
  }

  /** @returns {Job[]} Newest first. */
  all() {
    return [...this.#jobs.values()].reverse();
  }

  /** Jobs of a kind waiting to start, oldest first. */
  queued(kind) {
    return [...this.#jobs.values()].filter(
      (job) => job.kind === kind && job.status === JOB_STATUS.PENDING,
    );
  }

  /** Jobs of a kind holding a worker slot right now. */
  leasedCount(kind) {
    return [...this.#jobs.values()].filter(
      (job) => job.kind === kind && job.status === JOB_STATUS.LEASED,
    ).length;
  }
}

/** What the server says about a job in each state (OperatorJobStatus), with the file named when there is one. */
function operatorWords(job) {
  const name = (job.payload.relative_media_path ?? job.payload.path ?? "")
    .split(/[\\/]/)
    .pop();
  const subject = name ? ` for ${name}` : "";
  switch (job.status) {
    case JOB_STATUS.PENDING:
      return [
        `Queued${subject}.`,
        "Nothing to do. It starts when a worker is free.",
      ];
    case JOB_STATUS.LEASED:
      return [
        `Running${subject}.`,
        "Nothing to do. If it stays here too long, open the job record.",
      ];
    case JOB_STATUS.COMPLETED:
      return [
        `Finished${subject}.`,
        "Nothing to do. Open the processing record for the outcome.",
      ];
    case JOB_STATUS.CANCELLED:
      return [
        `Cancelled before it started${subject}.`,
        "Nothing to do. To process the file anyway, start it again from Files.",
      ];
    default:
      return [
        `Couldn't finish this job${subject}.`,
        "See Files or Jobs for why, fix it, then start it again.",
      ];
  }
}

/**
 * A job as the inspection endpoint lists it.
 * @param {Job} job
 */
export function jobRow(job) {
  const leased = job.status === JOB_STATUS.LEASED;
  const failed = job.status === JOB_STATUS.FAILED;
  const [message, nextAction] = operatorWords(job);
  return shaped("ProcessingJobInspectionRow", {
    id: job.id,
    job_kind: job.kind,
    status: job.status,
    dedupe_key: `${job.kind}:${job.payload.path ?? job.payload.relative_media_path ?? job.id}`,
    payload_json: JSON.stringify(job.payload),
    attempt_count: leased || failed ? 1 : 0,
    max_attempts: MAX_ATTEMPTS,
    lease_owner: leased ? WORKER_NAME : null,
    lease_expires_at: leased ? toWire(job.updatedAt + LEASE_MS) : null,
    last_error: job.lastError,
    created_at: toWire(job.createdAt),
    updated_at: toWire(job.updatedAt),
    operator_message: message,
    next_action: nextAction,
    technical_detail: job.lastError,
  });
}
