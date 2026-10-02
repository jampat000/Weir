/** The whole simulated install: its settings, the library, and the engine that keeps files moving. */
import { Artwork } from "./artwork/artwork.mjs";
import { posterTitles } from "./engine/catalogue.mjs";
import { Engine } from "./engine/engine.mjs";
import { LibraryFiles } from "./engine/library-files.mjs";
import { Machine } from "./machine/machine.mjs";
import { createRng } from "./engine/rng.mjs";
import { seedEngine } from "./engine/seed.mjs";
import { DEFAULT_SCENARIO, SCENARIOS } from "./scenarios.mjs";
import { createStore } from "./store.mjs";
import { TaskBook } from "./tasks/task-book.mjs";

export const DEFAULT_SEED = 20_261_002;
/** Mixed into the seed so the machine and the task list draw from sources of their own, and leave which files turn up as it was. */
const MACHINE_SALT = 0x4d41_4348;
const TASKS_SALT = 0x5441_534b;

/**
 * @typedef {object} Sim
 * @property {Engine} engine
 * @property {ReturnType<typeof createStore>} store
 * @property {LibraryFiles} libraryFiles
 * @property {Artwork} artwork
 * @property {Machine} machine The computer Weir runs on.
 * @property {TaskBook} tasks Weir's periodic tasks.
 * @property {() => number} now The current time in epoch ms.
 * @property {number} startedAt
 * @property {import("./scenarios.mjs").Scenario} scenario
 */

/**
 * @param {{ speed?: number, seed?: number, now?: () => number, withHistory?: boolean, scenario?: import("./scenarios.mjs").Scenario }} [options] `withHistory: false` starts with nothing done and nothing waiting; `scenario` says what kind of day it is.
 * @returns {Sim}
 */
export function createSim({
  speed = 1,
  seed = DEFAULT_SEED,
  now = Date.now,
  withHistory = true,
  scenario = SCENARIOS[DEFAULT_SCENARIO],
} = {}) {
  const startedAt = now();
  const store = createStore({ scenario, startedAt });
  const libraryFiles = new LibraryFiles(startedAt);
  const artwork = new Artwork({
    titles: posterTitles(),
    isEnabled: () => store.metadataProvider.artwork_enabled,
  });
  const engine = new Engine({
    rng: createRng(seed),
    speed,
    store,
    libraryFiles,
    scenario,
    startedAt,
  });
  const machine = new Machine({
    engine,
    rng: createRng(seed ^ MACHINE_SALT),
    startedAt,
  });
  const tasks = new TaskBook({
    engine,
    rng: createRng(seed ^ TASKS_SALT),
    startedAt,
    speed,
    failing: machine.profile.failingTask,
  });
  engine.onTick((nowMs) => {
    tasks.advance(nowMs);
    machine.advance(nowMs);
  });
  if (withHistory) seedEngine(engine, startedAt);
  return {
    engine,
    store,
    libraryFiles,
    artwork,
    machine,
    tasks,
    now,
    startedAt,
    scenario,
  };
}
