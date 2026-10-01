/**
 * A seeded random source, so a simulated session can be replayed and its state machine tested.
 * @typedef {object} Rng
 * @property {() => number} next A float in [0, 1).
 * @property {(low: number, high: number) => number} between A float in [low, high).
 * @property {(low: number, high: number) => number} int A whole number in [low, high].
 * @property {(probability: number) => boolean} chance True with the given probability.
 * @property {<T>(items: readonly T[]) => T} pick One element of a non-empty array.
 */

/**
 * Mulberry32: a tiny generator with a 32-bit state, plenty for picking sample titles.
 * @param {number} seed
 * @returns {Rng}
 */
export function createRng(seed) {
  let state = seed >>> 0;
  const next = () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  const between = (low, high) => low + next() * (high - low);
  return {
    next,
    between,
    int: (low, high) => Math.floor(between(low, high + 1)),
    chance: (probability) => next() < probability,
    pick: (items) => items[Math.floor(next() * items.length)],
  };
}
