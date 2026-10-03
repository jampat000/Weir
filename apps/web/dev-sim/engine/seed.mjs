/**
 * The state a session opens on, for the scenario it runs: a Weir that has been up for hours with finished work
 * behind it, files that wait on a person, and a board with files on every lane; or one where almost nothing has
 * happened yet.
 */
import { STATUS } from "./file.mjs";
import { VERDICT } from "./plan.mjs";
import { needsYouEvents } from "./seed-needs.mjs";
import { finishInThePast, release } from "./seed-support.mjs";
import {
  FOUR_K_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
} from "../fixtures/workflows.mjs";
import { SECOND_MS, MINUTE_MS } from "../wire-time.mjs";

/** How far back the finished work reaches. */
const HISTORY_MINUTES = 170;
const CLEAN_MINUTES_AGO = [24, 61, 97];
/** Which workflow each finished file belonged to, in turn: half TV, three tenths Movies, one each Kids and 4K. */
const HISTORY_WORKFLOWS = [
  TV_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
  FOUR_K_LIBRARY_ID,
  TV_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  TV_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
];
/** How far through writing each file already under way is, and which workflow it belongs to. */
const WRITING = [
  { workflowId: MOVIES_LIBRARY_ID, progress: 0.45 },
  { workflowId: TV_LIBRARY_ID, progress: 0.88 },
];
/** The workflow each waiting file belongs to, and how long ago it arrived. */
const WAITING = [
  { workflowId: TV_LIBRARY_ID, minutesAgo: 3 },
  { workflowId: FOUR_K_LIBRARY_ID, minutesAgo: 2 },
  { workflowId: TV_LIBRARY_ID, minutesAgo: 1 },
];
const HELD = [
  { workflowId: TV_LIBRARY_ID, remainingSeconds: 14 },
  { workflowId: KIDS_LIBRARY_ID, remainingSeconds: 38 },
];
const BLOCKED_REMAINING_SECONDS = 20;
const ALREADY_RIGHT_EVERY = 9;
const SIGN_IN_MINUTES_AGO = 140;
const SWEEP_MINUTES_AGO = 40;
const HANDBACK_SWEEP_MINUTES_AGO = 125;

/** Everything that happened before now, as [when, what], so it can be written down oldest first. */
function pastEvents(engine, nowMs, scenario) {
  const { files: fileCount, cleansPerWorkflow } = scenario.history;
  const events = [];
  for (let index = 0; index < fileCount; index += 1) {
    const at =
      nowMs - (2 + (index / fileCount) ** 1.4 * HISTORY_MINUTES) * MINUTE_MS;
    const verdict =
      index % ALREADY_RIGHT_EVERY === 4 ? VERDICT.ALREADY_RIGHT : VERDICT.CLEAN;
    const library = engine.library(
      HISTORY_WORKFLOWS[index % HISTORY_WORKFLOWS.length],
    );
    if (library)
      events.push([at, () => finishInThePast(engine, library, verdict, at)]);
  }
  events.push(...needsYouEvents(engine, nowMs, scenario.needsYou));
  for (const library of engine.store.libraries) {
    const files = engine.libraryFiles
      .list(library.id)
      .filter((file) => file.classification === "would_change");
    files.slice(0, cleansPerWorkflow).forEach((file, index) => {
      const at = nowMs - CLEAN_MINUTES_AGO[index] * MINUTE_MS;
      events.push([
        at,
        () => engine.recordPastClean(library.id, file.path, at),
      ]);
    });
  }
  events.push(...weirOwnEvents(engine, nowMs));
  return events.sort(([a], [b]) => a - b);
}

/** Things Weir did that are not about any one file: a sign-in, and the housekeeping it does on its own. */
function weirOwnEvents(engine, nowMs) {
  const note = (minutesAgo, entry) => {
    const at = nowMs - minutesAgo * MINUTE_MS;
    return [at, () => engine.activity.record(entry, at)];
  };
  return [
    note(SIGN_IN_MINUTES_AGO, {
      type: "auth.login_succeeded",
      title: "Sign-in finished",
      trigger: "manual",
      detail: { username: "admin" },
    }),
    note(SWEEP_MINUTES_AGO, {
      type: "processing.work_temp_stale_sweep_completed",
      title: "Temporary files cleanup finished",
      trigger: "scheduled",
      detail: { removed: 0 },
    }),
    note(HANDBACK_SWEEP_MINUTES_AGO, {
      type: "processing.unclaimed_handback_cleanup_completed",
      title: "Cleanup of copies nobody picked up finished",
      trigger: "scheduled",
      detail: { removed: 0 },
    }),
  ];
}

/** A pass already under way, `progress` of the way through writing. */
function startPartWay(engine, library, progress, nowMs) {
  const file = release(
    engine.admit(library, nowMs - 3 * MINUTE_MS, { verdict: VERDICT.CLEAN }),
  );
  engine.start(file, nowMs);
  const [checking, planning, writing] = file.run.timeline;
  const startedAt = nowMs - (checking.ms + planning.ms + writing.ms * progress);
  Object.assign(file.run, { startedAt, stageStartedAt: startedAt });
  file.passStartedAt = startedAt;
}

/** A download whose media manager is still importing the previous copy; none when its workflow has no manager asking. */
function layDownBlockedFile(engine, library, nowMs) {
  const manager = engine.managerFor(library);
  if (!manager || manager.last_test_ok === false) return;
  const blocked = engine.admit(library, nowMs - 45 * SECOND_MS, {
    verdict: VERDICT.CLEAN,
  });
  Object.assign(blocked, {
    status: STATUS.BLOCKED_UPSTREAM,
    blockedBy: manager.name,
    statusReason: `${manager.name} is still importing it.`,
    holdUntil: nowMs + (BLOCKED_REMAINING_SECONDS * SECOND_MS) / engine.speed,
  });
}

function layDownWorkInProgress(engine, nowMs) {
  for (const { workflowId, progress } of WRITING) {
    const library = engine.library(workflowId);
    if (library) startPartWay(engine, library, progress, nowMs);
  }
  for (const { workflowId, minutesAgo } of WAITING) {
    const library = engine.library(workflowId);
    if (!library) continue;
    const queuedAt = nowMs - minutesAgo * MINUTE_MS;
    release(engine.admit(library, queuedAt, { verdict: VERDICT.CLEAN }), {
      priority: queuedAt,
    });
  }
  for (const { workflowId, remainingSeconds } of HELD) {
    const library = engine.library(workflowId);
    if (!library) continue;
    const file = engine.admit(library, nowMs - 30 * SECOND_MS, {
      verdict: VERDICT.CLEAN,
    });
    Object.assign(file, {
      status: STATUS.ON_HOLD,
      blockedBy: null,
      holdUntil: nowMs + (remainingSeconds * SECOND_MS) / engine.speed,
    });
  }
  const tv = engine.library(TV_LIBRARY_ID);
  if (tv) layDownBlockedFile(engine, tv, nowMs);
}

/**
 * @param {import("./engine.mjs").Engine} engine
 * @param {number} nowMs
 */
export function seedEngine(engine, nowMs) {
  const { scenario } = engine;
  for (const [, happen] of pastEvents(engine, nowMs, scenario)) happen();
  if (scenario.workInProgress) layDownWorkInProgress(engine, nowMs);
  engine.tick(nowMs);
}
