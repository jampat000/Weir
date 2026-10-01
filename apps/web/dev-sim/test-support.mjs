/** Helpers for the simulation's tests: a clock the test moves, and a simulation that runs on it. */
import { createSim } from "./sim.mjs";

/** A start time on a day Weir has never seen, so no test depends on today's date. */
export const START_MS = Date.parse("2026-03-14T12:00:00Z");

export function fakeClock(startMs = START_MS) {
  let current = startMs;
  return {
    now: () => current,
    /** Moves time on by `ms`, ticking the engine each second on the way, as the real clock does. */
    advance(sim, ms) {
      const target = current + ms;
      while (current < target) {
        current = Math.min(current + 1000, target);
        sim.engine.tick(current);
      }
    },
  };
}

/** @param {{ speed?: number, seed?: number, withHistory?: boolean }} [options] A session starts empty unless `withHistory` is set. */
export function createTestSim(options = {}) {
  const clock = fakeClock();
  const sim = createSim({ withHistory: false, ...options, now: clock.now });
  return { sim, clock, advance: (ms) => clock.advance(sim, ms) };
}
