/** The whole simulated install: its settings, the library, and the engine that keeps files moving. */
import { Engine } from "./engine/engine.mjs";
import { LibraryFiles } from "./engine/library-files.mjs";
import { createRng } from "./engine/rng.mjs";
import { seedEngine } from "./engine/seed.mjs";
import { createStore } from "./store.mjs";

export const DEFAULT_SEED = 20_261_002;

/**
 * @typedef {object} Sim
 * @property {Engine} engine
 * @property {ReturnType<typeof createStore>} store
 * @property {LibraryFiles} libraryFiles
 * @property {() => number} now The current time in epoch ms.
 * @property {number} startedAt
 */

/**
 * @param {{ speed?: number, seed?: number, now?: () => number, withHistory?: boolean }} [options] `withHistory: false` starts with nothing done and nothing waiting.
 * @returns {Sim}
 */
export function createSim({
  speed = 1,
  seed = DEFAULT_SEED,
  now = Date.now,
  withHistory = true,
} = {}) {
  const startedAt = now();
  const store = createStore();
  const libraryFiles = new LibraryFiles(startedAt);
  const engine = new Engine({
    rng: createRng(seed),
    speed,
    store,
    libraryFiles,
  });
  if (withHistory) seedEngine(engine, startedAt);
  return { engine, store, libraryFiles, now, startedAt };
}
