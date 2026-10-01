/**
 * Public-domain films and open-licence shorts, classic public-domain television and a few fictional shows, which
 * the simulation "downloads" under release-style file names. Nothing here names a real person, hostname or
 * private release.
 */
import { posterIdOf } from "../artwork/poster-id.mjs";

import { FOUR_K_LIBRARY_ID, KIDS_LIBRARY_ID } from "../fixtures/workflows.mjs";

/** The release group stamped on every simulated file name. */
const RELEASE_GROUP = "WEIRSIM";

/** @typedef {{ title: string, year: number, resolution: 720 | 1080 | 2160, gigabytes: number }} FilmEntry */

/** @type {FilmEntry[]} */
export const FILMS = [
  { title: "The General", year: 1926, resolution: 1080, gigabytes: 5.8 },
  { title: "Sintel", year: 2010, resolution: 1080, gigabytes: 4.1 },
  { title: "Nosferatu", year: 1922, resolution: 1080, gigabytes: 6.2 },
  { title: "Metropolis", year: 1927, resolution: 1080, gigabytes: 9.4 },
  { title: "A Trip to the Moon", year: 1902, resolution: 720, gigabytes: 1.3 },
  {
    title: "Night of the Living Dead",
    year: 1968,
    resolution: 1080,
    gigabytes: 7.3,
  },
  { title: "Charade", year: 1963, resolution: 1080, gigabytes: 8.8 },
  { title: "His Girl Friday", year: 1940, resolution: 1080, gigabytes: 7.1 },
  { title: "Elephants Dream", year: 2006, resolution: 1080, gigabytes: 3.9 },
  { title: "Sherlock Jr.", year: 1924, resolution: 1080, gigabytes: 4.7 },
  { title: "The Kid", year: 1921, resolution: 1080, gigabytes: 5.1 },
  { title: "Safety Last", year: 1923, resolution: 1080, gigabytes: 4.4 },
  {
    title: "Plan 9 from Outer Space",
    year: 1959,
    resolution: 1080,
    gigabytes: 6.6,
  },
  { title: "Carnival of Souls", year: 1962, resolution: 1080, gigabytes: 6.0 },
  { title: "Detour", year: 1945, resolution: 1080, gigabytes: 5.2 },
  {
    title: "The Cabinet of Dr. Caligari",
    year: 1920,
    resolution: 1080,
    gigabytes: 5.5,
  },
];

/** What the 4K Movies workflow receives: UHD releases. @type {FilmEntry[]} */
export const FILMS_4K = [
  { title: "Tears of Steel", year: 2012, resolution: 2160, gigabytes: 14.6 },
  { title: "Cosmos Laundromat", year: 2015, resolution: 2160, gigabytes: 11.2 },
  { title: "Spring", year: 2019, resolution: 2160, gigabytes: 12.8 },
  { title: "Agent 327", year: 2017, resolution: 2160, gigabytes: 9.3 },
  { title: "Coffee Run", year: 2020, resolution: 2160, gigabytes: 8.1 },
  { title: "Sprite Fright", year: 2021, resolution: 2160, gigabytes: 15.2 },
  { title: "Charge", year: 2022, resolution: 2160, gigabytes: 10.4 },
];

/** What the Kids workflow receives: family films. @type {FilmEntry[]} */
export const FILMS_KIDS = [
  { title: "Big Buck Bunny", year: 2008, resolution: 1080, gigabytes: 3.4 },
  { title: "Caminandes", year: 2016, resolution: 1080, gigabytes: 2.2 },
  { title: "Wing It", year: 2023, resolution: 1080, gigabytes: 3.0 },
  { title: "Hero", year: 2018, resolution: 1080, gigabytes: 2.6 },
  { title: "Glass Half", year: 2015, resolution: 720, gigabytes: 0.9 },
  {
    title: "Snow Day Chronicles",
    year: 2019,
    resolution: 1080,
    gigabytes: 4.5,
  },
  { title: "Paper Boat", year: 2014, resolution: 1080, gigabytes: 3.7 },
];

/**
 * The films a workflow's folder receives: the Kids and 4K workflows have lists of their own, and every other
 * movie workflow takes the general one.
 * @param {{ id: number }} library
 */
export const filmsFor = (library) =>
  library.id === KIDS_LIBRARY_ID
    ? FILMS_KIDS
    : library.id === FOUR_K_LIBRARY_ID
      ? FILMS_4K
      : FILMS;

/** @typedef {{ title: string, year: number, resolution: 720 | 1080 | 2160, gigabytes: number, season: number }} ShowEntry */

/** @type {ShowEntry[]} */
export const SHOWS = [
  {
    title: "Harbour Lights",
    year: 2019,
    resolution: 1080,
    gigabytes: 2.2,
    season: 1,
  },
  {
    title: "The Twilight Zone",
    year: 1959,
    resolution: 720,
    gigabytes: 0.9,
    season: 3,
  },
  { title: "Dragnet", year: 1951, resolution: 1080, gigabytes: 1.6, season: 2 },
  {
    title: "Glass Orchard",
    year: 2021,
    resolution: 1080,
    gigabytes: 2.8,
    season: 1,
  },
  {
    title: "The Cisco Kid",
    year: 1950,
    resolution: 1080,
    gigabytes: 1.9,
    season: 2,
  },
  {
    title: "Copper Hollow",
    year: 2023,
    resolution: 2160,
    gigabytes: 5.4,
    season: 1,
  },
  {
    title: "The Lantern Keepers",
    year: 2016,
    resolution: 1080,
    gigabytes: 2.4,
    season: 4,
  },
];

/** The id a film's poster is served under. @param {FilmEntry} film */
export const filmPosterId = (film) =>
  posterIdOf({ mediaType: "movie", title: film.title, year: film.year });

/** The id a show's poster is served under: the series' own, whatever the episode. @param {ShowEntry} show */
export const showPosterId = (show) =>
  posterIdOf({ mediaType: "tv", title: show.title, year: show.year });

/** Every title the simulation can show, with the id its poster is served under. */
export function posterTitles() {
  return [
    ...[...FILMS, ...FILMS_4K, ...FILMS_KIDS].map((film) => ({
      id: filmPosterId(film),
      mediaType: /** @type {const} */ ("movie"),
      title: film.title,
      year: film.year,
    })),
    ...SHOWS.map((show) => ({
      id: showPosterId(show),
      mediaType: /** @type {const} */ ("tv"),
      title: show.title,
      year: show.year,
    })),
  ];
}

/** Audio and subtitle tracks the simulated sources carry, and the words a plan uses for them. */
export const FOREIGN_AUDIO = [
  "French 5.1 AC-3",
  "German 5.1 AC-3",
  "Italian 5.1 AC-3",
  "Spanish 2.0 AAC",
  "Japanese 2.0 AC-3",
];
export const SUBTITLE_LANGUAGES = [
  "fre",
  "ger",
  "ita",
  "spa",
  "jpn",
  "por",
  "dut",
  "swe",
  "nor",
  "dan",
];

const dotted = (title) =>
  title.replace(/[^A-Za-z0-9]+/g, ".").replace(/^\.|\.$/g, "");
const resolutionTag = (resolution) =>
  resolution === 2160
    ? "2160p.WEB-DL.DDP5.1.HEVC"
    : `${resolution}p.WEB-DL.DDP5.1.H.264`;

/** The folder-and-file path a download lands at: one folder named like the release, holding the one file. */
function releasePath(stem) {
  return `${stem}/${stem}.mkv`;
}

/** @param {FilmEntry} film */
export function filmPath(film) {
  const source =
    film.resolution === 2160
      ? "2160p.WEB-DL.DDP5.1.HEVC"
      : `${film.resolution}p.BluRay.DTS.x264`;
  return releasePath(
    `${dotted(film.title)}.${film.year}.${source}-${RELEASE_GROUP}`,
  );
}

/**
 * @param {ShowEntry} show
 * @param {number} episode
 */
export function episodePath(show, episode) {
  const code = `S${String(show.season).padStart(2, "0")}E${String(episode).padStart(2, "0")}`;
  return releasePath(
    `${dotted(show.title)}.${code}.${resolutionTag(show.resolution)}-${RELEASE_GROUP}`,
  );
}
