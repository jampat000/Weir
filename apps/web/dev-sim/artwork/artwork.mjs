/** The simulation's posters: which titles have one, the address each is served at, and the picture. */
import { generatedPoster } from "./generated-poster.mjs";

/** Where the server serves a poster; the id after it is the one `posterIdOf` makes. */
export const POSTER_PATH = "/api/v1/artwork/posters";

export class Artwork {
  /** @type {Map<string, { title: string, year: number }>} */
  #titles = new Map();
  /** The posters fetched from the metadata service so far; every other title is drawn. @type {Map<string, import("./gateway-posters.mjs").PosterImage>} */
  #fetched = new Map();

  /**
   * @param {{ titles: readonly (import("./poster-id.mjs").PosterTitle & { id: string })[] }} options
   */
  constructor({ titles }) {
    for (const { id, title, year } of titles)
      this.#titles.set(id, { title, year });
  }

  /** The address of a title's poster, or null when it has none. @param {string | null | undefined} id */
  urlFor(id) {
    return id && this.#titles.has(id)
      ? `${POSTER_PATH}/${id}`
      : null;
  }

  /** @param {string} id @param {import("./gateway-posters.mjs").PosterImage} image */
  keep(id, image) {
    this.#fetched.set(id, image);
  }

  /** The picture for a poster id, or null for an id nobody knows. @param {string} id */
  image(id) {
    const title = this.#titles.get(id);
    if (!title) return null;
    const fetched = this.#fetched.get(id);
    if (fetched) return fetched;
    const drawn = generatedPoster(title);
    return { contentType: drawn.contentType, body: Buffer.from(drawn.body) };
  }
}
