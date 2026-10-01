/** How a pass ends: the file's new state, what is written down about it, and what the media manager does next. */
import { REJECTED_BY_RULES, STATUS } from "./file.mjs";
import { JOB_STATUS } from "./jobs.mjs";
import { VERDICT } from "./plan.mjs";
import { EVENT_TYPE, passDetail, statusReasonFor } from "./records.mjs";
import { importRootOf, kindLabel } from "../fixtures/connections.mjs";
import { toWire } from "../wire-time.mjs";

const EXECUTION_FAILURE = "execution";
const FFMPEG_ERROR = "ffmpeg exited with code 1.";

const baseName = (path) => path.split("/").pop() ?? path;

export class Conclusions {
  #engine;

  /** @param {import("./engine.mjs").Engine} engine */
  constructor(engine) {
    this.#engine = engine;
  }

  /**
   * Ends a file's pass the way its plan says it ends.
   * @param {import("./file.mjs").SimFile} file
   * @param {number} nowMs
   */
  conclude(file, nowMs) {
    file.run = null;
    file.finishedAt = nowMs;
    file.updatedAt = nowMs;
    switch (file.plan.verdict) {
      case VERDICT.CLEAN:
        this.#cleaned(file, nowMs);
        break;
      case VERDICT.ALREADY_RIGHT:
        this.#alreadyRight(file, nowMs);
        break;
      case VERDICT.REJECTED:
        this.#rejected(file, nowMs);
        break;
      default:
        this.#failed(file, nowMs);
    }
    this.#engine.touch();
  }

  #record(file, nowMs, result) {
    file.outcomeDetail = passDetail(file, nowMs);
    file.statusReason = statusReasonFor(file);
    this.#engine.activity.record(
      {
        type: EVENT_TYPE.PASS_COMPLETED,
        title: "File processing finished",
        libraryId: file.libraryId,
        relativePath: file.relativePath,
        detail: file.outcomeDetail,
        result,
      },
      nowMs,
    );
  }

  #finishJob(file, nowMs, status, error = null) {
    if (file.jobId !== null)
      this.#engine.jobs.setStatus(file.jobId, status, nowMs, error);
  }

  #cleaned(file, nowMs) {
    const outputFolder =
      this.#engine.library(file.libraryId)?.output_folder ?? "";
    file.status = STATUS.PROCESSED;
    file.handback = {
      output_path: `${outputFolder}\\${baseName(file.relativePath)}`,
      written_at: toWire(nowMs),
      outcome: null,
      outcome_by: null,
      outcome_at: null,
      imported_path: null,
      outcome_reason: null,
      released_at: null,
      settled_at: null,
      release_note: null,
    };
    const library = this.#engine.library(file.libraryId);
    file.handbackSettlesAt =
      library && this.#engine.managerFor(library)
        ? nowMs + this.#engine.importDelay()
        : null;
    this.#record(file, nowMs, "success");
    this.#finishJob(file, nowMs, JOB_STATUS.COMPLETED);
  }

  #alreadyRight(file, nowMs) {
    file.status = STATUS.PROCESSED;
    this.#record(file, nowMs, "success");
    this.#finishJob(file, nowMs, JOB_STATUS.COMPLETED);
  }

  #rejected(file, nowMs) {
    file.status = STATUS.REJECTED;
    file.failureClass = REJECTED_BY_RULES;
    this.#record(file, nowMs, "success");
    this.#finishJob(file, nowMs, JOB_STATUS.COMPLETED);
  }

  #failed(file, nowMs) {
    file.status = STATUS.FAILED;
    file.failureClass = EXECUTION_FAILURE;
    file.failureAttempts = 1;
    this.#record(file, nowMs, "failed");
    this.#finishJob(file, nowMs, JOB_STATUS.FAILED, FFMPEG_ERROR);
  }

  /**
   * The media manager takes the copy Weir handed back and says what it did.
   * @param {import("./file.mjs").SimFile} file
   * @param {number} nowMs
   */
  settleHandback(file, nowMs) {
    const library = this.#engine.library(file.libraryId);
    const manager = library ? this.#engine.managerFor(library) : null;
    if (!manager) return;
    const by = kindLabel(manager.kind);
    const stem = baseName(file.relativePath);
    file.handback = {
      ...file.handback,
      outcome: "imported",
      outcome_by: by,
      outcome_at: toWire(nowMs),
      imported_path: `${importRootOf(manager)}\\${stem}`,
      released_at: toWire(nowMs),
      settled_at: toWire(nowMs),
      release_note: `${by} moved Weir's copy into its library, so there was nothing for Weir to remove.`,
    };
    file.handbackSettlesAt = null;
    this.#engine.activity.record(
      {
        type: EVENT_TYPE.HANDBACK_OUTCOME,
        title: "What happened to a cleaned copy",
        libraryId: file.libraryId,
        relativePath: file.relativePath,
        detail: {
          outcome: "imported",
          outcome_by: by,
          relative_media_path: file.relativePath,
        },
        trigger: "webhook",
      },
      nowMs,
    );
    this.#engine.touch();
  }
}
