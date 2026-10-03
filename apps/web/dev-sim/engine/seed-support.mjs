/** What laying down the past needs: putting a file in the queue, and living a file's pass to its end back in time. */
import { STATUS } from "./file.mjs";
import { SECOND_MS, MINUTE_MS } from "../wire-time.mjs";

const PASS_SECONDS = 40;
const HANDBACK_SECONDS = 6;

/** Puts a freshly admitted file straight into the queue, with nothing holding it. */
export function release(file, fields = {}) {
  return Object.assign(file, {
    status: STATUS.WAITING,
    statusReason: "",
    holdUntil: null,
    blockedBy: null,
    ...fields,
  });
}

/**
 * A file that arrived two minutes before `finishedAt`, and had its pass end then with the given verdict.
 * @param {import("./engine.mjs").Engine} engine
 */
export function finishInThePast(engine, library, verdict, finishedAt) {
  const file = release(
    engine.admit(library, finishedAt - 2 * MINUTE_MS, { verdict }),
  );
  file.passStartedAt = finishedAt - PASS_SECONDS * SECOND_MS;
  engine.conclude(file, finishedAt);
  if (file.handback)
    engine.settle(file, finishedAt + HANDBACK_SECONDS * SECOND_MS);
}
