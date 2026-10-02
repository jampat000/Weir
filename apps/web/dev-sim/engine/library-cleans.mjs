/** Library cleans: Weir cleaning a file where it already sits in a library, now and then and when a person asks. */
import { CONNECTION_KIND } from "./connection-health.mjs";
import { JOB_KIND, JOB_STATUS } from "./jobs.mjs";
import { EVENT_TYPE } from "./records.mjs";
import { toWire } from "../wire-time.mjs";

/** How long a clean takes and how long Weir waits between starting one on its own, at normal speed. */
const CLEAN_MS = [5_000, 11_000];
const FIRST_CLEAN_MS = 25_000;
const CLEAN_GAP_MS = [45_000, 95_000];
/** No further clean is queued on its own while this many are waiting. */
const MOST_QUEUED = 2;
const MEGABYTE = 1024 ** 2;
const GIGABYTE = 1024 ** 3;

/** @param {number} bytes */
function sizeWords(bytes) {
  return bytes >= GIGABYTE
    ? `${(bytes / GIGABYTE).toFixed(1)} GB`
    : `${Math.round(bytes / MEGABYTE)} MB`;
}

export class CleanRuns {
  #engine;
  #rng;
  #speed;
  /** @type {Map<number, { endsAt: number }>} */
  #running = new Map();
  #nextAt = 0;
  #nextRecordId = 1;
  #rotation = 0;
  /** What History lists: the newest clean of each file, newest first. */
  records = /** @type {Record<string, any>[]} */ ([]);

  /**
   * @param {import("./engine.mjs").Engine} engine
   * @param {import("./rng.mjs").Rng} rng
   * @param {number} speed
   */
  constructor(engine, rng, speed) {
    this.#engine = engine;
    this.#rng = rng;
    this.#speed = speed;
  }

  #queuedPaths() {
    return new Set(
      this.#engine.jobs
        .all()
        .filter(
          (job) =>
            job.kind === JOB_KIND.LIBRARY_CLEAN &&
            [JOB_STATUS.PENDING, JOB_STATUS.LEASED].includes(job.status),
        )
        .map((job) => String(job.payload.path)),
    );
  }

  /**
   * @param {number} libraryId
   * @param {string} path
   * @param {number} nowMs
   * @returns {boolean} False when the file is unknown or already has a clean queued.
   */
  queue(libraryId, path, nowMs) {
    const known = this.#engine.libraryFiles
      .list(libraryId)
      .some((file) => file.path === path);
    if (!known || this.#queuedPaths().has(path)) return false;
    this.#engine.jobs.create(
      JOB_KIND.LIBRARY_CLEAN,
      { library_id: libraryId, path },
      nowMs,
    );
    return true;
  }

  /** Writes down a clean that finished at `nowMs`, without running one. @param {number} libraryId @param {string} path @param {number} nowMs */
  recordPast(libraryId, path, nowMs) {
    this.#finish(
      this.#engine.jobs.create(
        JOB_KIND.LIBRARY_CLEAN,
        { library_id: libraryId, path },
        nowMs,
      ),
      nowMs,
    );
  }

  /** How many cleans are running in a workflow. @param {number} libraryId */
  runningIn(libraryId) {
    return [...this.#running.keys()].filter(
      (id) => this.#engine.jobs.get(id)?.payload.library_id === libraryId,
    ).length;
  }

  /** A worker takes a queued clean. @param {import("./jobs.mjs").Job} job @param {number} nowMs */
  start(job, nowMs) {
    const library = this.#engine.library(job.payload.library_id);
    const manager = library ? this.#engine.managerFor(library) : null;
    if (manager)
      this.#engine.connections.reach(CONNECTION_KIND.MANAGER, manager, nowMs);
    this.#engine.jobs.setStatus(job.id, JOB_STATUS.LEASED, nowMs);
    this.#running.set(job.id, {
      endsAt: nowMs + this.#rng.between(...CLEAN_MS) / this.#speed,
    });
    this.#engine.touch();
  }

  /** @param {number} nowMs */
  advance(nowMs) {
    for (const [jobId, run] of this.#running) {
      if (nowMs < run.endsAt) continue;
      this.#running.delete(jobId);
      this.#finish(this.#engine.jobs.get(jobId), nowMs);
    }
  }

  /** Queues the next clean of a library Weir looks after on its own, once enough time has passed. @param {number} nowMs */
  scheduleNext(nowMs) {
    const idleSpeed = this.#speed * this.#engine.scenario.pace;
    if (this.#nextAt === 0) this.#nextAt = nowMs + FIRST_CLEAN_MS / idleSpeed;
    if (nowMs < this.#nextAt) return;
    this.#nextAt = nowMs + this.#rng.between(...CLEAN_GAP_MS) / idleSpeed;
    if (
      this.#engine.pause.paused ||
      this.#engine.jobs.queued(JOB_KIND.LIBRARY_CLEAN).length >= MOST_QUEUED
    )
      return;
    const libraries = this.#engine.store.libraries.filter(
      (library) => library.enabled,
    );
    if (libraries.length === 0) return;
    const library = libraries[this.#rotation++ % libraries.length];
    const file = this.#engine.libraryFiles.nextToClean(
      library.id,
      this.#queuedPaths(),
    );
    if (file && this.queue(library.id, file.path, nowMs)) this.#engine.touch();
  }

  #finish(job, nowMs) {
    if (!job) return;
    const { library_id: libraryId, path } =
      /** @type {{ library_id: number, path: string }} */ (job.payload);
    const file = this.#engine.libraryFiles
      .list(libraryId)
      .find((candidate) => candidate.path === path);
    const library = this.#engine.library(libraryId);
    const detail = file
      ? `Removed ${file.removed_audio_tracks} audio and ${file.removed_subtitle_tracks} subtitle tracks and saved ${sizeWords(file.estimated_bytes_saved)}.`
      : "Cleaned.";
    this.#engine.libraryFiles.markCleaned(libraryId, path, nowMs);
    this.#engine.jobs.setStatus(job.id, JOB_STATUS.COMPLETED, nowMs);
    const record = {
      kind: "library_clean",
      id: this.#nextRecordId++,
      library_id: libraryId,
      library_name: library?.name ?? "Workflow",
      relative_path: path,
      outcome: "cleaned",
      detail,
      trigger: "scheduled",
      recorded_at: toWire(nowMs),
    };
    this.records.unshift(record);
    this.#engine.activity.record(
      {
        type: EVENT_TYPE.LIBRARY_FILE_CLEANED,
        title: `Cleaned ${path.split("\\").pop()}`,
        libraryId,
        relativePath: path,
        posterId: file?.poster_id ?? null,
        detail: { relative_path: path, outcome: "cleaned", detail },
        trigger: "scheduled",
      },
      nowMs,
    );
    this.#engine.touch();
  }
}
