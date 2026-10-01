/**
 * The simulated Weir: files arrive in the watched folders, wait, work through the steps of a pass, finish, and are
 * handed back. It follows the same rules the real server does: it honours the pause, "files at once" and each
 * workflow's own limit, and a rejected or failed file waits for a person. Time only moves when `tick` is given a
 * time, so a test can drive it with a clock of its own.
 */
import { ActivityLog } from "./activity-log.mjs";
import { needsAttention, STATUS } from "./file.mjs";
import { Intake } from "./intake.mjs";
import { JobBook, JOB_KIND, JOB_STATUS } from "./jobs.mjs";
import { CleanRuns } from "./library-cleans.mjs";
import {
  advanceRun,
  currentStage,
  liveEntry,
  percentOf,
  STAGE,
  startRun,
} from "./pass.mjs";
import { makePlan, VERDICT } from "./plan.mjs";
import { Conclusions } from "./conclusions.mjs";
import { EVENT_TYPE } from "./records.mjs";
import { MINUTE_MS } from "../wire-time.mjs";

const WORKER_SLOTS = 4;
/** How long after a file is handed back its media manager takes the copy. */
const IMPORT_DELAY_MS = [4_000, 9_000];

/**
 * @typedef {object} EngineOptions
 * @property {import("./rng.mjs").Rng} rng
 * @property {number} speed How many times faster than normal the simulation runs.
 * @property {ReturnType<import("../store.mjs").createStore>} store
 * @property {import("./library-files.mjs").LibraryFiles} libraryFiles
 */

export class Engine {
  /** @type {Map<number, import("./file.mjs").SimFile>} */
  files = new Map();
  jobs = new JobBook();
  activity = new ActivityLog();
  /** The state of the pause switch. */
  pause = {
    paused: false,
    until: /** @type {number | null} */ (null),
    scanWhilePaused: true,
  };
  /** Bumped on every change a database write would make; the stream sends it so open screens refresh. */
  revision = 0;
  /** Files a person chose to remove from History but keep on disk: the file, and how the kept-files list shows it. */
  kept =
    /** @type {{ file: import("./file.mjs").SimFile, listing: Record<string, unknown> }[]} */ ([]);

  #rng;
  #speed;
  #store;
  #intake;
  #conclusions;
  #cleans;
  #changed = false;
  #listeners = new Set();

  /** @param {EngineOptions} options */
  constructor({ rng, speed, store, libraryFiles }) {
    this.#rng = rng;
    this.#speed = speed;
    this.#store = store;
    this.libraryFiles = libraryFiles;
    this.#intake = new Intake(this, rng, speed);
    this.#conclusions = new Conclusions(this);
    this.#cleans = new CleanRuns(this, rng, speed);
  }

  get speed() {
    return this.#speed;
  }

  get store() {
    return this.#store;
  }

  /** Called once after each tick that changed something. @param {(change: { revision: number, latestEventId: number }) => void} listener */
  onChange(listener) {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  /** Marks the state as changed, so the next notification goes out. */
  touch() {
    this.#changed = true;
  }

  /** The library a workflow id names, or null when it has been deleted. */
  library(libraryId) {
    return (
      this.#store.libraries.find((library) => library.id === libraryId) ?? null
    );
  }

  /** How many files may be worked on at once: the setting, held to the worker slots. */
  slots() {
    return Math.min(this.#store.operator.max_concurrent_files, WORKER_SLOTS);
  }

  /** Files being written, plus library cleans running. */
  running() {
    return (
      this.#processingFiles().length +
      this.jobs.leasedCount(JOB_KIND.LIBRARY_CLEAN)
    );
  }

  #processingFiles() {
    return [...this.files.values()].filter(
      (file) => file.status === STATUS.PROCESSING,
    );
  }

  /** Files waiting for a free slot, soonest first. */
  waitingFiles() {
    return [...this.files.values()]
      .filter((file) => file.status === STATUS.WAITING)
      .sort((a, b) => a.priority - b.priority);
  }

  /** Advances the simulation to `nowMs`. @param {number} nowMs */
  tick(nowMs) {
    this.#resumeWhenDue(nowMs);
    this.#intake.arrive(nowMs);
    this.#intake.releaseHolds(nowMs);
    this.#advancePasses(nowMs);
    this.#cleans.advance(nowMs);
    this.#settleHandbacks(nowMs);
    this.#cleans.scheduleNext(nowMs);
    this.#startWork(nowMs);
    if (!this.#changed) return;
    this.#changed = false;
    this.revision += 1;
    for (const listener of this.#listeners)
      listener({
        revision: this.revision,
        latestEventId: this.activity.latestId(),
      });
  }

  /**
   * A new download lands in a library's watched folder.
   * @param {Record<string, any>} library
   * @param {number} nowMs
   * @param {{ verdict?: string }} [options]
   */
  admit(library, nowMs, options) {
    return this.#intake.admit(library, nowMs, options);
  }

  #resumeWhenDue(nowMs) {
    if (
      this.pause.paused &&
      this.pause.until !== null &&
      nowMs >= this.pause.until
    ) {
      this.pause = { ...this.pause, paused: false, until: null };
      this.touch();
    }
  }

  #startWork(nowMs) {
    if (this.pause.paused) return;
    while (this.running() < this.slots()) {
      const next = this.#nextToStart();
      if (!next) return;
      if (next.file) this.start(next.file, nowMs);
      else this.#cleans.start(next.job, nowMs);
    }
  }

  /** The waiting file or library clean that has waited longest and whose workflow has a slot free. */
  #nextToStart() {
    const candidates = [
      ...this.waitingFiles().map((file) => ({
        file,
        job: null,
        at: file.priority,
        libraryId: file.libraryId,
      })),
      ...this.jobs.queued(JOB_KIND.LIBRARY_CLEAN).map((job) => ({
        file: null,
        job,
        at: job.createdAt,
        libraryId: job.payload.library_id,
      })),
    ].sort((a, b) => a.at - b.at);
    return (
      candidates.find((candidate) =>
        this.#libraryHasSlot(candidate.libraryId),
      ) ?? null
    );
  }

  #libraryHasSlot(libraryId) {
    const limit = this.library(libraryId)?.max_concurrent_files ?? 0;
    if (limit <= 0) return true;
    const files = this.#processingFiles().filter(
      (file) => file.libraryId === libraryId,
    ).length;
    return files + this.#cleans.runningIn(libraryId) < limit;
  }

  /**
   * A worker takes a waiting file and starts its pass.
   * @param {import("./file.mjs").SimFile} file
   * @param {number} nowMs
   */
  start(file, nowMs) {
    const verdict = file.plan.verdict;
    Object.assign(file, {
      status: STATUS.PROCESSING,
      statusReason: "",
      passStartedAt: nowMs,
      lastAttemptAt: nowMs,
      updatedAt: nowMs,
    });
    file.run = startRun(verdict, file.sizeBytes, nowMs, this.#rng, this.#speed);
    if (file.jobId !== null)
      this.jobs.setStatus(file.jobId, JOB_STATUS.LEASED, nowMs);
    this.activity.record(
      {
        type: EVENT_TYPE.PASS_PROGRESS,
        title: "File processing",
        libraryId: file.libraryId,
        relativePath: file.relativePath,
        result: "running",
        detail: {
          status: "processing",
          message: "Weir is checking the file.",
          relative_media_path: file.relativePath,
          percent: null,
        },
      },
      nowMs,
    );
    this.touch();
  }

  #advancePasses(nowMs) {
    for (const file of this.#processingFiles()) {
      if (!file.run) continue;
      const { moved, finished } = advanceRun(file.run, nowMs);
      if (moved) {
        file.updatedAt = nowMs;
        this.touch();
      }
      const failsNow =
        file.plan.failsAtPercent !== null &&
        currentStage(file.run) === STAGE.WRITING &&
        (percentOf(file.run, nowMs) ?? 0) >= file.plan.failsAtPercent;
      if (failsNow || finished) this.#conclusions.conclude(file, nowMs);
    }
  }

  #settleHandbacks(nowMs) {
    for (const file of this.files.values()) {
      if (
        file.handback &&
        file.handbackSettlesAt !== null &&
        nowMs >= file.handbackSettlesAt
      )
        this.#conclusions.settleHandback(file, nowMs);
    }
  }

  /** How long after a hand-back the manager takes the copy. */
  importDelay() {
    return this.#rng.between(...IMPORT_DELAY_MS) / this.#speed;
  }

  /** Concludes a file's pass at `nowMs`, however it is going to end. Used to lay down history. */
  conclude(file, nowMs) {
    this.#conclusions.conclude(file, nowMs);
  }

  /** The media manager takes the copy handed back for a file, at `nowMs`. Used to lay down history. */
  settle(file, nowMs) {
    this.#conclusions.settleHandback(file, nowMs);
  }

  /** A library clean that finished at `nowMs`, in the past. Used to lay down history. */
  recordPastClean(libraryId, path, nowMs) {
    this.#cleans.recordPast(libraryId, path, nowMs);
  }

  /** The live-progress entry of every file with a pass running. @param {number} nowMs */
  liveProgress(nowMs) {
    return this.#processingFiles().flatMap((file) =>
      file.run ? [liveEntry(file, file.run, nowMs)] : [],
    );
  }

  // Commands a person can give.

  /**
   * @param {{ paused: boolean, pauseForMinutes?: number | null, scanWhilePaused?: boolean }} request
   * @param {number} nowMs
   */
  setPause({ paused, pauseForMinutes, scanWhilePaused }, nowMs) {
    this.pause = {
      paused,
      until:
        paused && pauseForMinutes ? nowMs + pauseForMinutes * MINUTE_MS : null,
      scanWhilePaused: scanWhilePaused ?? this.pause.scanWhilePaused,
    };
    this.touch();
  }

  /**
   * Puts a failed, rejected or finished file back in the queue to be worked on again.
   * @param {number} fileId
   * @param {number} nowMs
   * @returns {boolean}
   */
  requeue(fileId, nowMs) {
    const file = this.files.get(fileId);
    if (!file || file.status === STATUS.PROCESSING) return false;
    const verdict = needsAttention(file) ? VERDICT.CLEAN : file.plan.verdict;
    Object.assign(file, {
      status: STATUS.WAITING,
      statusReason: "",
      failureClass: null,
      failureAttempts: 0,
      plan: makePlan(this.#rng, verdict, file.sizeBytes),
      outcomeDetail: null,
      handback: null,
      handbackSettlesAt: null,
      finishedAt: null,
      updatedAt: nowMs,
      priority: nowMs,
    });
    file.jobId = this.jobs.create(
      JOB_KIND.FILE_PASS,
      {
        relative_media_path: file.relativePath,
        library_id: file.libraryId,
        media_scope: file.mediaType,
      },
      nowMs,
    ).id;
    this.touch();
    return true;
  }

  /** @param {number} fileId */
  moveToTop(fileId) {
    const file = this.files.get(fileId);
    if (!file || file.status !== STATUS.WAITING) return false;
    file.priority =
      Math.min(...this.waitingFiles().map((waiting) => waiting.priority)) - 1;
    this.touch();
    return true;
  }

  /** @param {number} fileId */
  remove(fileId) {
    const file = this.files.get(fileId);
    if (!file) return null;
    this.files.delete(fileId);
    this.touch();
    return file;
  }

  /**
   * Queues library cleans for the given files.
   * @param {number} libraryId
   * @param {string[]} paths
   * @param {number} nowMs
   * @returns {number} How many were queued.
   */
  queueLibraryCleans(libraryId, paths, nowMs) {
    const queued = paths.filter((path) =>
      this.#cleans.queue(libraryId, path, nowMs),
    );
    if (queued.length > 0) this.touch();
    return queued.length;
  }

  /** @param {number} jobId @param {number} nowMs */
  cancelJob(jobId, nowMs) {
    const job = this.jobs.get(jobId);
    if (!job || job.status !== JOB_STATUS.PENDING) return false;
    this.jobs.setStatus(jobId, JOB_STATUS.CANCELLED, nowMs);
    if (job.kind === JOB_KIND.FILE_PASS) {
      for (const file of this.files.values()) {
        if (file.jobId !== jobId) continue;
        Object.assign(file, {
          status: STATUS.CANCELLED,
          statusReason: "Cancelled before Weir started on it.",
          updatedAt: nowMs,
        });
      }
    }
    this.touch();
    return true;
  }

  /** Library cleans recorded so far, newest first, for History. */
  get cleanRecords() {
    return this.#cleans.records;
  }
}
