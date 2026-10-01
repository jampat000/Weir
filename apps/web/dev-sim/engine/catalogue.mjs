/**
 * Fictional and public-domain titles the simulation "downloads", with release-style file names.
 * Nothing here names a real person, hostname or private release.
 */

/** The release group stamped on every simulated file name. */
const RELEASE_GROUP = "WEIRSIM";

/** @typedef {{ title: string, year: number, resolution: 720 | 1080 | 2160, gigabytes: number }} FilmEntry */

/** @type {FilmEntry[]} */
export const FILMS = [
  { title: "The General", year: 1926, resolution: 1080, gigabytes: 5.8 },
  { title: "Sintel", year: 2010, resolution: 1080, gigabytes: 4.1 },
  { title: "Big Buck Bunny", year: 2008, resolution: 1080, gigabytes: 3.4 },
  { title: "Tears of Steel", year: 2012, resolution: 2160, gigabytes: 14.6 },
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
  { title: "Cosmos Laundromat", year: 2015, resolution: 2160, gigabytes: 11.2 },
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
  { title: "Spring", year: 2019, resolution: 2160, gigabytes: 12.8 },
  {
    title: "The Cabinet of Dr. Caligari",
    year: 1920,
    resolution: 1080,
    gigabytes: 5.5,
  },
];

/** @typedef {{ title: string, resolution: 720 | 1080 | 2160, gigabytes: number, season: number }} ShowEntry */

/** @type {ShowEntry[]} */
export const SHOWS = [
  { title: "Harbour Lights", resolution: 1080, gigabytes: 2.2, season: 1 },
  { title: "Northbound", resolution: 720, gigabytes: 0.9, season: 3 },
  { title: "Starlit Relay", resolution: 1080, gigabytes: 1.6, season: 2 },
  { title: "Glass Orchard", resolution: 1080, gigabytes: 2.8, season: 1 },
  { title: "Ember and Ash", resolution: 1080, gigabytes: 1.9, season: 2 },
  { title: "Copper Hollow", resolution: 2160, gigabytes: 5.4, season: 1 },
  { title: "The Lantern Keepers", resolution: 1080, gigabytes: 2.4, season: 4 },
];

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
