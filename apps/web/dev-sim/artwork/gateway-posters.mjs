/**
 * Real posters for the simulation's titles, from Deluno's metadata service: one lookup per title, ever. Each answer
 * is kept in a folder of the simulation's own, so a later start asks nothing, and a title the service has no poster
 * for is remembered too. Offline, or when the service says it is busy, the lookups stop and the simulation draws its
 * own posters.
 */
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

export const GATEWAY_URL =
  "https://deluno-metadata-gateway.ejmdigital.workers.dev";

/** The most lookups one start-up makes, and the gap between them: well under the service's per-address budget. */
export const MAX_LOOKUPS = 40;
export const LOOKUP_GAP_MS = 1000;

const POSTER_SIZE = "w342";
const POSTER_SIZES = /\/(w92|w185|w342|w500|w780|w1280|original)\//;
const TRUSTED_IMAGE_HOSTS = new Set(["image.tmdb.org"]);
const SERVICE_ERROR = 500;
const IMAGE_FILE = ".jpg";
const MISS_FILE = ".none";

/** @typedef {import("./poster-id.mjs").PosterTitle & { id: string }} CatalogueTitle */
/** @typedef {{ contentType: string, body: Buffer }} PosterImage */

/** Thrown when the service cannot be asked any more: no network, or it has asked to be left alone. */
class ServiceUnavailable extends Error {}

const normalised = (text) =>
  text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();

/** The result that is this very title and year, and has a poster; the first search hit is not trusted blindly. */
function matchingResult(results, { title, year }) {
  return (
    results.find(
      (result) =>
        result.posterUrl &&
        normalised(String(result.title)) === normalised(title) &&
        String(result.year) === String(year),
    ) ?? null
  );
}

/** Where the 342px poster is, for a poster address on the gateway or TMDb; nothing for any other host. */
function posterAddress(posterUrl, gatewayUrl) {
  try {
    const url = new URL(posterUrl);
    const trusted =
      url.protocol === "https:" &&
      (TRUSTED_IMAGE_HOSTS.has(url.hostname) ||
        url.hostname === new URL(gatewayUrl).hostname);
    return trusted ? url.href.replace(POSTER_SIZES, `/${POSTER_SIZE}/`) : null;
  } catch {
    return null;
  }
}

async function fetchOrThrow(fetchImpl, url) {
  let response;
  try {
    response = await fetchImpl(url);
  } catch (error) {
    throw new ServiceUnavailable("The metadata service cannot be reached.", {
      cause: error,
    });
  }
  if (response.status >= SERVICE_ERROR || response.status === 429)
    throw new ServiceUnavailable("The metadata service is busy.");
  return response;
}

/**
 * The poster for one title, or null when the service has none.
 * @param {CatalogueTitle} title
 * @param {{ fetchImpl: typeof fetch, gatewayUrl: string }} service
 * @returns {Promise<PosterImage | null>}
 */
async function lookUp(title, { fetchImpl, gatewayUrl }) {
  const query = new URLSearchParams({
    mediaType: title.mediaType === "tv" ? "tv" : "movies",
    query: title.title,
    year: String(title.year),
  });
  const search = await fetchOrThrow(
    fetchImpl,
    `${gatewayUrl}/metadata/search?${query}`,
  );
  if (!search.ok) return null;
  const { results = [] } = await search.json();
  const found = matchingResult(results, title);
  const address = found && posterAddress(found.posterUrl, gatewayUrl);
  if (!address) return null;
  const image = await fetchOrThrow(fetchImpl, address);
  const contentType = image.headers.get("content-type") ?? "";
  if (!image.ok || !contentType.startsWith("image/")) return null;
  return { contentType, body: Buffer.from(await image.arrayBuffer()) };
}

/** What an earlier start kept for this title: its poster, a note that there is none, or nothing yet. */
async function kept(cacheDir, id) {
  try {
    return {
      contentType: "image/jpeg",
      body: await readFile(join(cacheDir, `${id}${IMAGE_FILE}`)),
    };
  } catch {
    // Not kept as an image: look for the note that the service has none.
  }
  try {
    await readFile(join(cacheDir, `${id}${MISS_FILE}`));
    return null;
  } catch {
    return undefined;
  }
}

/**
 * Looks up the posters the folder does not have yet, one at a time, and hands each to `onPoster` as it arrives.
 * @param {readonly CatalogueTitle[]} titles
 * @param {{ cacheDir: string, onPoster: (id: string, image: PosterImage) => void, log?: (line: string) => void, fetchImpl?: typeof fetch, gatewayUrl?: string, pause?: (ms: number) => Promise<void> }} options
 */
export async function loadGatewayPosters(
  titles,
  {
    cacheDir,
    onPoster,
    log = () => {},
    fetchImpl = fetch,
    gatewayUrl = GATEWAY_URL,
    pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  },
) {
  await mkdir(cacheDir, { recursive: true });
  let lookups = 0;
  for (const title of titles) {
    const earlier = await kept(cacheDir, title.id);
    if (earlier) onPoster(title.id, earlier);
    if (earlier !== undefined) continue;
    if (lookups >= MAX_LOOKUPS) break;
    if (lookups > 0) await pause(LOOKUP_GAP_MS);
    lookups += 1;
    try {
      const poster = await lookUp(title, { fetchImpl, gatewayUrl });
      if (poster) {
        await writeFile(
          join(cacheDir, `${title.id}${IMAGE_FILE}`),
          poster.body,
        );
        onPoster(title.id, poster);
      } else {
        await writeFile(join(cacheDir, `${title.id}${MISS_FILE}`), "");
      }
    } catch (error) {
      if (!(error instanceof ServiceUnavailable)) throw error;
      log(`[dev-sim] ${error.message} Using drawn posters for the rest.`);
      return;
    }
  }
  log(
    `[dev-sim] Posters ready (${lookups} looked up, the rest kept from earlier).`,
  );
}
