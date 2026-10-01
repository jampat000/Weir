/** The whole simulated install: its settings, the library, and the engine that keeps files moving. */
import { Artwork } from "./artwork/artwork.mjs";
import { posterTitles } from "./engine/catalogue.mjs";
import { Engine } from "./engine/engine.mjs";
import { LibraryFiles } from "./engine/library-files.mjs";
import { createRng } from "./engine/rng.mjs";
import { seedEngine } from "./engine/seed.mjs";
import { DEFAULT_SCENARIO, SCENARIOS } from "./scenarios.mjs";
import { createStore } from "./store.mjs";

export const DEFAULT_SEED = 20_261_002;

/**
 * @typedef {object} Sim
 * @property {Engine} engine
 * @property {ReturnType<typeof createStore>} store
 * @property {LibraryFiles} libraryFiles
 * @property {Artwork} artwork
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
  if (withHistory) seedEngine(engine, startedAt);
  return { engine, store, libraryFiles, artwork, now, startedAt, scenario };
}
