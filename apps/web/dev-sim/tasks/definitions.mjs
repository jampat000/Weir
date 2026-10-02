/**
 * Every periodic task Weir runs, as the settings have it right now: a scan and a library clean for each workflow, and
 * Weir's own tasks (see weir-tasks.mjs). A task that is switched off in Settings is not listed.
 */
import { SECOND_MS } from "../wire-time.mjs";
import { CleanTask } from "./clean-task.mjs";
import { TimedTask } from "./timed-task.mjs";
import { weirTasks } from "./weir-tasks.mjs";

const scanKey = (libraryId) => `scan-${libraryId}`;
const cleanKey = (libraryId) => `library-clean-${libraryId}`;

const SCAN_RUN_MS = [1_200, 3_200];
const DEFAULT_SCAN_SECONDS = 300;

/**
 * What every task is built from.
 * @typedef {object} TaskContext
 * @property {import("../engine/engine.mjs").Engine} engine
 * @property {import("../engine/rng.mjs").Rng} rng
 * @property {number} startedAt
 * @property {number} speed
 * @property {import("../machine/profiles.mjs").Profile["failingTask"]} failing The task the scenario makes fail, if any.
 */

/** The scan of one workflow's watched folder. `place` spreads the workflows' scans across the interval so they do not all fall at once. */
function scanTask(context, library, place) {
  const { engine, rng, startedAt, speed } = context;
  const intervalSeconds = () =>
    engine.library(library.id)?.scan_interval_seconds ?? DEFAULT_SCAN_SECONDS;
  const intervalMs = (intervalSeconds() * SECOND_MS) / speed;
  const nextRunAt = startedAt + intervalMs * place;
  return new TimedTask({
    speed,
    intervalSeconds,
    runMs: SCAN_RUN_MS,
    rng,
    last: { at: nextRunAt - intervalMs, ok: true },
    nextRunAt,
    canRun: () => !engine.pause.paused || engine.pause.scanWhilePaused,
  });
}

/** The tasks of one workflow. */
function workflowTasks(context, library, place) {
  return [
    {
      key: scanKey(library.id),
      label: `Scan ${library.name}`,
      create: () => scanTask(context, library, place),
    },
    {
      key: cleanKey(library.id),
      label: `Clean ${library.name} library`,
      create: () =>
        new CleanTask({ engine: context.engine, libraryId: library.id }),
    },
  ];
}

/**
 * The tasks wanted now: each one's key and label, and the way to make it the first time it is wanted.
 * @param {TaskContext} context
 * @returns {{ key: string, label: string, create: () => import("./task-book.mjs").Task }[]}
 */
export function wantedTasks(context) {
  const libraries = context.engine.store.libraries.filter(
    (library) => library.enabled,
  );
  return [
    ...libraries.flatMap((library, index) =>
      workflowTasks(context, library, (index + 1) / (libraries.length + 1)),
    ),
    ...weirTasks(context),
  ];
}
