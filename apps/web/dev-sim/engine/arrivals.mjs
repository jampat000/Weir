/** What turns up in the watched folders: public-domain films and fictional episodes, and how each will turn out. */
import {
  FILMS,
  SHOWS,
  episodePath,
  filmPath,
  filmPosterId,
  showPosterId,
} from "./catalogue.mjs";
import { VERDICT } from "./plan.mjs";

const GIGABYTE = 1024 ** 3;
/** How far a file's size strays from its title's usual size. */
const SIZE_JITTER = 0.12;
const FIRST_EPISODE_CEILING = 8;

/** The share of arrivals the rules turn away, the share that fail part-way, and the share that already match the rules. */
const REJECTED_SHARE = 0.05;
const FAILED_SHARE = 0.035;
const ALREADY_RIGHT_SHARE = 0.12;

/**
 * @param {import("./rng.mjs").Rng} rng
 * @param {{ rejected?: number, failed?: number }} [shares] The share of arrivals to reject and to fail, where a scenario sets its own.
 */
export function chooseVerdict(
  rng,
  { rejected = REJECTED_SHARE, failed = FAILED_SHARE } = {},
) {
  const roll = rng.next();
  if (roll < rejected) return VERDICT.REJECTED;
  if (roll < rejected + failed) return VERDICT.FAILS;
  if (roll < rejected + failed + ALREADY_RIGHT_SHARE)
    return VERDICT.ALREADY_RIGHT;
  return VERDICT.CLEAN;
}

/**
 * @typedef {object} Download
 * @property {string} relativePath
 * @property {number} sizeBytes
 * @property {number} height
 * @property {number} durationSeconds
 * @property {string} posterId The id the title's poster is served under.
 */

export class Arrivals {
  #rng;
  /** The titles not yet used in this pass through each list. @type {Map<readonly import("./catalogue.mjs").FilmEntry[], import("./catalogue.mjs").FilmEntry[]>} */
  #filmsLeft = new Map();
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

  /** The next film of a list: every title once, in a shuffled order, before any comes round again. */
  #nextFilm(titles) {
    let left = this.#filmsLeft.get(titles) ?? [];
    if (left.length === 0)
      left = [...titles].sort(() => this.#rng.next() - 0.5);
    this.#filmsLeft.set(titles, left);
    return left.pop();
  }

  /**
   * @param {readonly import("./catalogue.mjs").FilmEntry[]} [titles] The workflow's titles.
   * @returns {Download}
   */
  film(titles = FILMS) {
    const film = this.#nextFilm(titles);
    return {
      relativePath: filmPath(film),
      posterId: filmPosterId(film),
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
      posterId: showPosterId(show),
      sizeBytes: this.#sized(show.gigabytes),
      height: show.resolution,
      durationSeconds: this.#rng.int(1500, 3300),
    };
  }
}
