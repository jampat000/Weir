/** The opaque, stable id a title's poster is served under: the same for every file of that title. */

/** @typedef {{ mediaType: "movie" | "tv", title: string, year: number }} PosterTitle */

/** @param {PosterTitle} title */
export function posterIdOf({ mediaType, title, year }) {
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
  return `${mediaType}-${slug}-${year}`;
}
