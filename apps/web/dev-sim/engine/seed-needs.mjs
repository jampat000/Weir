/**
 * The files already waiting on a person when a session opens, each for a reason the server words as it would: the
 * rules turned it away for its language, a pass failed while writing, a file is held because Weir cannot open it,
 * and a file is skipped because its path matches one of the workflow's rules.
 */
import { STATUS } from "./file.mjs";
import { VERDICT } from "./plan.mjs";
import { finishInThePast } from "./seed-support.mjs";
import {
  FOUR_K_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
} from "../fixtures/workflows.mjs";
import { MINUTE_MS } from "../wire-time.mjs";

/** The span of the past, in minutes before now, the waiting files are spread across. */
const OLDEST_MINUTES_AGO = 150;
const NEWEST_MINUTES_AGO = 18;

/** Which workflows each kind of file turns up in, in turn. */
const REJECTED_IN = [
  MOVIES_LIBRARY_ID,
  TV_LIBRARY_ID,
  FOUR_K_LIBRARY_ID,
  KIDS_LIBRARY_ID,
  TV_LIBRARY_ID,
];
const FAILED_IN = [TV_LIBRARY_ID, MOVIES_LIBRARY_ID, FOUR_K_LIBRARY_ID];
const HELD_IN = [MOVIES_LIBRARY_ID, KIDS_LIBRARY_ID];
const PATH_RULE_IN = [KIDS_LIBRARY_ID, MOVIES_LIBRARY_ID];

/** Offsets that keep one kind's files from landing on another's minute. */
const KIND_OFFSET_MINUTES = {
  rejected: 0,
  failed: 4,
  held: 7,
  pathRule: 11,
};

const EXCLUDED_BY_PATH_RULE =
  "Skipped because its path matches this workflow's exclude patterns.";
const EXTRAS_FOLDER = "Extras";
const EXTRAS_FILE = "Behind.the.Scenes.mkv";

/** The minute, before now, the `index`th of `count` files of a kind arrived. */
function minutesAgo(kind, index, count) {
  const span = OLDEST_MINUTES_AGO - NEWEST_MINUTES_AGO;
  return (
    NEWEST_MINUTES_AGO +
    (span * (index + 0.5)) / Math.max(count, 1) +
    KIND_OFFSET_MINUTES[kind]
  );
}

/**
 * A download Weir will not touch until a person acts: no pass is queued for it and nothing is counting down.
 * @param {import("./engine.mjs").Engine} engine
 */
function leaveForAPerson(engine, library, at, { status, statusReason, path }) {
  const file = engine.admit(library, at, { verdict: VERDICT.CLEAN });
  engine.jobs.remove(file.jobId);
  Object.assign(file, {
    status,
    statusReason: statusReason(file),
    holdUntil: null,
    blockedBy: null,
    jobId: null,
    relativePath: path ? path(file) : file.relativePath,
    lastAttemptAt: null,
  });
  return file;
}

const lockedByAnotherProgram = (library) => (file) =>
  `Weir could not open this file for reading — it is usually locked by whatever is still writing it. The system reported: The process cannot access the file '${library.watched_folder}\\${file.relativePath.replaceAll("/", "\\")}' because it is being used by another process.`;

/** The folder of a release, with the file moved into an extras folder inside it. */
const intoExtras = (file) =>
  `${file.relativePath.split("/")[0]}/${EXTRAS_FOLDER}/${EXTRAS_FILE}`;

/**
 * Everything that is already waiting on a person, as [when, what], so it can be written down with the rest of the past.
 * @param {import("./engine.mjs").Engine} engine
 * @param {number} nowMs
 * @param {import("../scenarios.mjs").Scenario["needsYou"]} needsYou
 * @returns {[number, () => void][]}
 */
export function needsYouEvents(engine, nowMs, needsYou) {
  const events = [];
  const add = (kind, count, libraries, happen) => {
    for (let index = 0; index < count; index += 1) {
      const library = engine.library(libraries[index % libraries.length]);
      const at = nowMs - minutesAgo(kind, index, count) * MINUTE_MS;
      if (library) events.push([at, () => happen(library, at)]);
    }
  };
  add("rejected", needsYou.languageRejected, REJECTED_IN, (library, at) =>
    finishInThePast(engine, library, VERDICT.REJECTED, at),
  );
  add("failed", needsYou.failedWhileWriting, FAILED_IN, (library, at) =>
    finishInThePast(engine, library, VERDICT.FAILS, at),
  );
  add("held", needsYou.onHoldStuck, HELD_IN, (library, at) =>
    leaveForAPerson(engine, library, at, {
      status: STATUS.ON_HOLD,
      statusReason: lockedByAnotherProgram(library),
    }),
  );
  add("pathRule", needsYou.pathRule, PATH_RULE_IN, (library, at) =>
    leaveForAPerson(engine, library, at, {
      status: STATUS.SKIPPED,
      statusReason: () => EXCLUDED_BY_PATH_RULE,
      path: intoExtras,
    }),
  );
  return events;
}
