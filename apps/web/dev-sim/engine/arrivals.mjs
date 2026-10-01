/** What turns up in the watched folders: public-domain films and fictional episodes, and how each will turn out. */
import { FILMS, SHOWS, episodePath, filmPath } from "./catalogue.mjs";
import { VERDICT } from "./plan.mjs";

const GIGABYTE = 1024 ** 3;
/** How far a file's size strays from its title's usual size. */
const SIZE_JITTER = 0.12;
const FIRST_EPISODE_CEILING = 8;

/** The share of arrivals the rules turn away, the share that fail part-way, and the share that already match the rules. */
const REJECTED_SHARE = 0.05;
const FAILED_SHARE = 0.035;
const ALREADY_RIGHT_SHARE = 0.12;

/** @param {import("./rng.mjs").Rng} rng */
export function chooseVerdict(rng) {
  const roll = rng.next();
  if (roll < REJECTED_SHARE) return VERDICT.REJECTED;
  if (roll < REJECTED_SHARE + FAILED_SHARE) return VERDICT.FAILS;
  if (roll < REJECTED_SHARE + FAILED_SHARE + ALREADY_RIGHT_SHARE)
    return VERDICT.ALREADY_RIGHT;
  return VERDICT.CLEAN;
}

/**
 * @typedef {object} Download
 * @property {string} relativePath
 * @property {number} sizeBytes
 * @property {number} height
 * @property {number} durationSeconds
 */

export class Arrivals {
  #rng;
  #filmsLeft = [];
  /** @type {Map<string, number>} */
  #episodes = new Map();

  /** @param {import("./rng.mjs").Rng} rng */
  constructor(rng) {
    this.#rng = rng;
  }

  #sized(gigabytes) {
    return Math.round(
      gigabytes * GIGABYTE * (1 + this.#rng.between(-SIZE_JITTER, SIZE_JITTER)),
    );
  }

  /** The next film: every title once, in a shuffled order, before any comes round again. */
  #nextFilm() {
    if (this.#filmsLeft.length === 0) {
      this.#filmsLeft = [...FILMS].sort(() => this.#rng.next() - 0.5);
    }
    return this.#filmsLeft.pop();
  }

  /** @returns {Download} */
  film() {
    const film = this.#nextFilm();
    return {
      relativePath: filmPath(film),
      sizeBytes: this.#sized(film.gigabytes),
      height: film.resolution,
      durationSeconds: this.#rng.int(5400, 9000),
    };
  }

  /** @returns {Download} */
  episode() {
    const show = this.#rng.pick(SHOWS);
    const episode =
      (this.#episodes.get(show.title) ??
        this.#rng.int(1, FIRST_EPISODE_CEILING)) + 1;
    this.#episodes.set(show.title, episode);
    return {
      relativePath: episodePath(show, episode),
      sizeBytes: this.#sized(show.gigabytes),
      height: show.resolution,
      durationSeconds: this.#rng.int(1500, 3300),
    };
  }
}
