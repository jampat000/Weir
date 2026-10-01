/**
 * One processing pass over a file: the steps it goes through, how long each takes, and what the live progress frame
 * says at any moment. The step names and the status each carries are the server's (`PassStages`, #827), so the board
 * keys on the same words it does against a real Weir.
 */
import { VERDICT } from "./plan.mjs";
import { SECOND_MS } from "../wire-time.mjs";

export const STAGE = Object.freeze({
  CHECKING: "checking",
  PLANNING: "planning",
  WRITING: "writing",
  VERIFYING: "verifying",
  HANDING_BACK: "handing_back",
});

/** `processing` while a file is read and written; `finishing` once only the final checks are left. */
const STATUS_BY_STAGE = {
  [STAGE.CHECKING]: "processing",
  [STAGE.PLANNING]: "processing",
  [STAGE.WRITING]: "processing",
  [STAGE.VERIFYING]: "finishing",
  [STAGE.HANDING_BACK]: "finishing",
};

const MESSAGE_BY_STAGE = {
  [STAGE.CHECKING]: "Weir is checking the file.",
  [STAGE.PLANNING]: "Weir is working out which tracks to keep.",
  [STAGE.WRITING]: "Weir is writing the cleaned-up file.",
  [STAGE.VERIFYING]: "The file was placed. Weir is doing final safety checks.",
  [STAGE.HANDING_BACK]: "Weir is handing the finished file back.",
};

/** How long each step takes at normal speed. Writing grows with the size of the file. */
const CHECKING_MS = 3200;
const PLANNING_MS = 2400;
const VERIFYING_MS = 3000;
const HANDING_BACK_MS = 2600;
const WRITING_MS_PER_GIGABYTE = 3200;
const WRITING_MIN_MS = 10_000;
const WRITING_MAX_MS = 38_000;
const GIGABYTE = 1024 ** 3;

const FULL = 100;
/** ffmpeg's reported speed, as a multiple of playback. */
const SPEED_LOW = 220;
const SPEED_SPREAD = 760;

/** @param {number} sizeBytes */
function writingMs(sizeBytes) {
  return Math.min(
    WRITING_MAX_MS,
    Math.max(WRITING_MIN_MS, (sizeBytes / GIGABYTE) * WRITING_MS_PER_GIGABYTE),
  );
}

/**
 * The steps a pass takes for its verdict, each with how long it lasts. A file the rules turn away stops after
 * planning, and a file that already matches skips the write.
 * @param {string} verdict
 * @param {number} sizeBytes
 * @returns {{ stage: string, ms: number }[]}
 */
export function timelineFor(verdict, sizeBytes) {
  const preparation = [
    { stage: STAGE.CHECKING, ms: CHECKING_MS },
    { stage: STAGE.PLANNING, ms: PLANNING_MS },
  ];
  if (verdict === VERDICT.REJECTED) return preparation;
  if (verdict === VERDICT.ALREADY_RIGHT)
    return [...preparation, { stage: STAGE.HANDING_BACK, ms: HANDING_BACK_MS }];
  return [
    ...preparation,
    { stage: STAGE.WRITING, ms: writingMs(sizeBytes) },
    { stage: STAGE.VERIFYING, ms: VERIFYING_MS },
    { stage: STAGE.HANDING_BACK, ms: HANDING_BACK_MS },
  ];
}

/**
 * @typedef {object} Run
 * @property {{ stage: string, ms: number }[]} timeline
 * @property {number} index The step the pass is on.
 * @property {number} stageStartedAt
 * @property {number} startedAt
 * @property {number} speedTimes ffmpeg's speed, for the label.
 */

/**
 * @param {string} verdict
 * @param {number} sizeBytes
 * @param {number} nowMs
 * @param {import("./rng.mjs").Rng} rng
 * @param {number} speed The simulation's pace; a step lasts its normal time divided by this.
 * @returns {Run}
 */
export function startRun(verdict, sizeBytes, nowMs, rng, speed) {
  return {
    timeline: timelineFor(verdict, sizeBytes).map((step) => ({
      ...step,
      ms: step.ms / speed,
    })),
    index: 0,
    stageStartedAt: nowMs,
    startedAt: nowMs,
    speedTimes: Math.round(SPEED_LOW + rng.next() * SPEED_SPREAD),
  };
}

/** @param {Run} run */
export const currentStage = (run) => run.timeline[run.index].stage;

/** How far through its own step the pass is, from 0 to 1. */
function stageFraction(run, nowMs) {
  const { ms } = run.timeline[run.index];
  return Math.min(1, Math.max(0, (nowMs - run.stageStartedAt) / ms));
}

/**
 * The percent the live frame reports: null while the pass is only reading the file, climbing while it writes, and
 * full once only checks are left.
 * @param {Run} run
 * @param {number} nowMs
 */
export function percentOf(run, nowMs) {
  const stage = currentStage(run);
  if (stage === STAGE.WRITING)
    return Math.round(stageFraction(run, nowMs) * FULL * 10) / 10;
  return stage === STAGE.VERIFYING || stage === STAGE.HANDING_BACK
    ? FULL
    : null;
}

/**
 * Moves a pass on to every step whose time has run out.
 * @param {Run} run
 * @param {number} nowMs
 * @returns {{ moved: boolean, finished: boolean }}
 */
export function advanceRun(run, nowMs) {
  let moved = false;
  for (;;) {
    const { ms } = run.timeline[run.index];
    if (nowMs - run.stageStartedAt < ms) return { moved, finished: false };
    if (run.index === run.timeline.length - 1) return { moved, finished: true };
    run.stageStartedAt += ms;
    run.index += 1;
    moved = true;
  }
}

/**
 * One file's entry in the `processing.progress` frame, and the `progress_*` fields of the file list.
 * @param {{ relativePath: string, plan: import("./plan.mjs").Plan }} file
 * @param {Run} run
 * @param {number} nowMs
 */
export function liveEntry(file, run, nowMs) {
  const stage = currentStage(run);
  const percent = percentOf(run, nowMs);
  const planned = stage !== STAGE.CHECKING && stage !== STAGE.PLANNING;
  const stepRemainingMs =
    run.timeline[run.index].ms * (1 - stageFraction(run, nowMs));
  return {
    relative_path: file.relativePath,
    status: STATUS_BY_STAGE[stage],
    stage,
    percent,
    eta_seconds:
      stage === STAGE.WRITING
        ? Math.max(0, Math.round(stepRemainingMs / SECOND_MS))
        : null,
    message: MESSAGE_BY_STAGE[stage],
    speed: stage === STAGE.WRITING ? `${run.speedTimes}x` : null,
    elapsed_seconds: Math.round((nowMs - run.startedAt) / SECOND_MS),
    removed_audio: planned ? file.plan.removedAudio : [],
    removed_subtitles: planned ? file.plan.removedSubtitles : [],
  };
}
