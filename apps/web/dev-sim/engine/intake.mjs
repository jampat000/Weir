/** Downloads turning up in the watched folders, and the holds that keep each one waiting until it is safe to touch. */
import { Arrivals, chooseVerdict } from "./arrivals.mjs";
import { filmsFor } from "./catalogue.mjs";
import { CONNECTION_KIND } from "./connection-health.mjs";
import { createFile, STATUS } from "./file.mjs";
import { JOB_KIND } from "./jobs.mjs";
import { makePlan } from "./plan.mjs";
import {
  FOUR_K_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
} from "../fixtures/workflows.mjs";
import { SECOND_MS } from "../wire-time.mjs";

/** How long a new file takes to turn up, at normal speed. */
const ARRIVAL_GAP_MS = [7_000, 17_000];
/** New files stop arriving while this many are already waiting to be worked on. */
const BACKLOG_LIMIT = 9;
const BACKLOG_RETRY_MS = 5_000;
/** The share of arrivals a media manager is still importing, and how long that lasts. */
const BLOCKED_SHARE = 0.12;
const BLOCKED_MS = [12_000, 26_000];
/** The share of arrivals that are TV episodes rather than films. */
const TV_SHARE = 0.62;
const MOST_SECONDS_SINCE_CHANGE = 4;
/** How often a film turns up in each movie workflow, next to the others; a workflow made from Settings counts once. */
const FILM_WORKFLOW_WEIGHT = {
  [MOVIES_LIBRARY_ID]: 4,
  [KIDS_LIBRARY_ID]: 1,
  [FOUR_K_LIBRARY_ID]: 2,
};
const DEFAULT_WORKFLOW_WEIGHT = 1;

export class Intake {
  #engine;
  #rng;
  #speed;
  #arrivals;
  #nextFileId = 100;
  #nextArrivalAt = 0;

  /**
   * @param {import("./engine.mjs").Engine} engine
   * @param {import("./rng.mjs").Rng} rng
   * @param {number} speed
   */
  constructor(engine, rng, speed) {
    this.#engine = engine;
    this.#rng = rng;
    this.#speed = speed;
    this.#arrivals = new Arrivals(rng);
  }

  /** The files queued or held for a reason that will pass; one held until a person looks at it is not in the way. */
  #backlog() {
    const waiting = [STATUS.ON_HOLD, STATUS.BLOCKED_UPSTREAM, STATUS.WAITING];
    return [...this.#engine.files.values()].filter(
      (file) =>
        waiting.includes(file.status) &&
        !(file.status === STATUS.ON_HOLD && file.holdUntil === null),
    ).length;
  }

  /** How long the next download takes to turn up: a quiet scenario spaces them further apart. */
  #arrivalGap() {
    return (
      this.#rng.between(...ARRIVAL_GAP_MS) /
      (this.#speed * this.#engine.scenario.pace)
    );
  }

  /** One of the workflows, chosen by how often downloads reach it. @param {Record<string, any>[]} libraries */
  #weightedPick(libraries) {
    const weightOf = (library) =>
      FILM_WORKFLOW_WEIGHT[library.id] ?? DEFAULT_WORKFLOW_WEIGHT;
    const total = libraries.reduce(
      (sum, library) => sum + weightOf(library),
      0,
    );
    let roll = this.#rng.next() * total;
    return (
      libraries.find((library) => (roll -= weightOf(library)) < 0) ??
      libraries[0]
    );
  }

  #libraryForNextArrival() {
    const wanted = this.#rng.chance(TV_SHARE) ? "tv" : "movie";
    const enabled = this.#engine.store.libraries.filter(
      (library) => library.enabled,
    );
    const ofKind = enabled.filter((library) => library.media_type === wanted);
    return ofKind.length > 0
      ? this.#weightedPick(ofKind)
      : (enabled[0] ?? null);
  }

  /** Brings in a new download when one is due, unless Weir has stopped looking or has plenty waiting. @param {number} nowMs */
  arrive(nowMs) {
    if (this.#nextArrivalAt === 0)
      this.#nextArrivalAt = nowMs + this.#arrivalGap();
    if (nowMs < this.#nextArrivalAt) return;
    const { pause } = this.#engine;
    const scanning = !pause.paused || pause.scanWhilePaused;
    if (!scanning || this.#backlog() >= BACKLOG_LIMIT) {
      this.#nextArrivalAt = nowMs + BACKLOG_RETRY_MS / this.#speed;
      return;
    }
    this.#nextArrivalAt = nowMs + this.#arrivalGap();
    const library = this.#libraryForNextArrival();
    if (library) this.admit(library, nowMs);
  }

  /**
   * A new download lands in a library's watched folder.
   * @param {Record<string, any>} library
   * @param {number} nowMs
   * @param {{ verdict?: string }} [options]
   */
  admit(library, nowMs, options = {}) {
    const download =
      library.media_type === "tv"
        ? this.#arrivals.episode()
        : this.#arrivals.film(filmsFor(library));
    const verdict =
      options.verdict ??
      chooseVerdict(this.#rng, this.#engine.scenario.arrivalShares);
    const rulesName = this.#engine.store.ruleSets.find(
      (ruleSet) => ruleSet.id === library.rule_set_id,
    )?.name;
    const id = this.#nextFileId++;
    const file = createFile({
      id,
      libraryId: library.id,
      libraryName: library.name,
      mediaType: library.media_type,
      relativePath: download.relativePath,
      posterId: download.posterId,
      sizeBytes: download.sizeBytes,
      durationSeconds: download.durationSeconds,
      video: {
        codec: download.height >= 2160 ? "hevc" : "h264",
        width: Math.round((download.height * 16) / 9),
        height: download.height,
      },
      plan: makePlan(this.#rng, verdict, download.sizeBytes, { rulesName }),
      createdAt: nowMs,
      updatedAt: nowMs,
      sizeChangedAt:
        nowMs - this.#rng.int(1, MOST_SECONDS_SINCE_CHANGE) * SECOND_MS,
      priority: nowMs,
    });
    this.#engine.files.set(id, file);
    file.jobId = this.#engine.jobs.create(
      JOB_KIND.FILE_PASS,
      {
        relative_media_path: file.relativePath,
        library_id: library.id,
        media_scope: file.mediaType,
      },
      nowMs,
    ).id;
    this.#hold(file, library, nowMs);
    this.#engine.touch();
    return file;
  }

  #hold(file, library, nowMs) {
    const manager = this.#engine.managerFor(library);
    const asking = manager?.last_test_ok !== false;
    if (manager)
      this.#engine.connections.reach(CONNECTION_KIND.MANAGER, manager, nowMs);
    if (manager && asking && this.#rng.chance(BLOCKED_SHARE)) {
      file.status = STATUS.BLOCKED_UPSTREAM;
      file.blockedBy = manager.name;
      file.statusReason = `${file.blockedBy} is still importing it.`;
      file.holdUntil = nowMs + this.#rng.between(...BLOCKED_MS) / this.#speed;
      return;
    }
    file.status = STATUS.ON_HOLD;
    file.statusReason = `This file changed too recently. Weir waits ${library.ready_after_seconds}s after the last change.`;
    file.holdUntil =
      nowMs + (library.ready_after_seconds * SECOND_MS) / this.#speed;
  }

  /** Lets a file go to the queue once its hold has run out. @param {number} nowMs */
  releaseHolds(nowMs) {
    for (const file of this.#engine.files.values()) {
      const holding =
        file.status === STATUS.ON_HOLD ||
        file.status === STATUS.BLOCKED_UPSTREAM;
      if (!holding || file.holdUntil === null || nowMs < file.holdUntil)
        continue;
      Object.assign(file, {
        status: STATUS.WAITING,
        statusReason: "",
        holdUntil: null,
        blockedBy: null,
        updatedAt: nowMs,
        priority: Math.max(file.priority, nowMs),
      });
      this.#engine.touch();
    }
  }
}
