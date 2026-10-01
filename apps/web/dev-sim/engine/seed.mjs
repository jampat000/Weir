/**
 * A Weir that has been running all afternoon: a few hours of finished work behind it, a couple of files that need a
 * person, and a board that already has files on every lane when the page first opens.
 */
import { STATUS } from "./file.mjs";
import { VERDICT } from "./plan.mjs";
import { SECOND_MS, MINUTE_MS } from "../wire-time.mjs";

/** How far back the finished work reaches, and how many files it holds. */
const HISTORY_MINUTES = 170;
const HISTORY_FILES = 46;
const CLEANS_PER_WORKFLOW = 3;
const CLEAN_MINUTES_AGO = [24, 61, 97];
/** Where each file that needs a person sits in the past. */
const FAILED_MINUTES_AGO = 31;
const REJECTED_MINUTES_AGO = [58, 112];
/** How far through writing each file already under way is. */
const WRITING_PROGRESS = [0.45, 0.88];
const WAITING_MINUTES_AGO = [3, 2, 1];
const HOLD_REMAINING_SECONDS = [14, 38];
const BLOCKED_REMAINING_SECONDS = 20;
const TV_OUT_OF_THREE = 2;
const PASS_SECONDS = 40;
const HANDBACK_SECONDS = 6;
const ALREADY_RIGHT_EVERY = 9;
const SIGN_IN_MINUTES_AGO = 140;
const SWEEP_MINUTES_AGO = 40;
const HANDBACK_SWEEP_MINUTES_AGO = 125;

const libraryOfKind = (engine, mediaType) =>
  engine.store.libraries.find((library) => library.media_type === mediaType);

/** Puts a freshly admitted file straight into the queue, with nothing holding it. */
function release(file, fields = {}) {
  return Object.assign(file, {
    status: STATUS.WAITING,
    statusReason: "",
    holdUntil: null,
    blockedBy: null,
    ...fields,
  });
}

function finishInThePast(engine, library, verdict, finishedAt) {
  const file = release(
    engine.admit(library, finishedAt - 2 * MINUTE_MS, { verdict }),
  );
  file.passStartedAt = finishedAt - PASS_SECONDS * SECOND_MS;
  engine.conclude(file, finishedAt);
  if (file.handback)
    engine.settle(file, finishedAt + HANDBACK_SECONDS * SECOND_MS);
}

/** Everything that happened before now, as [when, what], so it can be written down oldest first. */
function pastEvents(engine, nowMs) {
  const tv = libraryOfKind(engine, "tv");
  const movies = libraryOfKind(engine, "movie");
  const events = [];
  for (let index = 0; index < HISTORY_FILES; index += 1) {
    const at =
      nowMs -
      (2 + (index / HISTORY_FILES) ** 1.4 * HISTORY_MINUTES) * MINUTE_MS;
    const verdict =
      index % ALREADY_RIGHT_EVERY === 4 ? VERDICT.ALREADY_RIGHT : VERDICT.CLEAN;
    const library = index % 3 < TV_OUT_OF_THREE ? tv : movies;
    events.push([at, () => finishInThePast(engine, library, verdict, at)]);
  }
  const failedAt = nowMs - FAILED_MINUTES_AGO * MINUTE_MS;
  events.push([
    failedAt,
    () => finishInThePast(engine, tv, VERDICT.FAILS, failedAt),
  ]);
  REJECTED_MINUTES_AGO.forEach((minutes, index) => {
    const at = nowMs - minutes * MINUTE_MS;
    events.push([
      at,
      () =>
        finishInThePast(
          engine,
          index === 0 ? movies : tv,
          VERDICT.REJECTED,
          at,
        ),
    ]);
  });
  for (const library of engine.store.libraries) {
    const files = engine.libraryFiles
      .list(library.id)
      .filter((file) => file.classification === "would_change");
    files.slice(0, CLEANS_PER_WORKFLOW).forEach((file, index) => {
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

function layDownWorkInProgress(engine, nowMs) {
  const tv = libraryOfKind(engine, "tv");
  const movies = libraryOfKind(engine, "movie");
  startPartWay(engine, movies, WRITING_PROGRESS[0], nowMs);
  startPartWay(engine, tv, WRITING_PROGRESS[1], nowMs);
  WAITING_MINUTES_AGO.forEach((minutes, index) => {
    const queuedAt = nowMs - minutes * MINUTE_MS;
    release(
      engine.admit(index === 1 ? movies : tv, queuedAt, {
        verdict: VERDICT.CLEAN,
      }),
      { priority: queuedAt },
    );
  });
  HOLD_REMAINING_SECONDS.forEach((seconds, index) => {
    const file = engine.admit(
      index === 0 ? tv : movies,
      nowMs - 30 * SECOND_MS,
      { verdict: VERDICT.CLEAN },
    );
    Object.assign(file, {
      status: STATUS.ON_HOLD,
      blockedBy: null,
      holdUntil: nowMs + (seconds * SECOND_MS) / engine.speed,
    });
  });
  const manager = engine.store.managers.find(
    (candidate) => candidate.kind === "sonarr",
  );
  const blocked = engine.admit(tv, nowMs - 45 * SECOND_MS, {
    verdict: VERDICT.CLEAN,
  });
  Object.assign(blocked, {
    status: STATUS.BLOCKED_UPSTREAM,
    blockedBy: manager?.name ?? null,
    statusReason: `${manager?.name ?? "Your media manager"} is still importing it.`,
    holdUntil: nowMs + (BLOCKED_REMAINING_SECONDS * SECOND_MS) / engine.speed,
  });
}

/**
 * @param {import("./engine.mjs").Engine} engine
 * @param {number} nowMs
 */
export function seedEngine(engine, nowMs) {
  for (const [, happen] of pastEvents(engine, nowMs)) happen();
  layDownWorkInProgress(engine, nowMs);
  engine.tick(nowMs);
}
