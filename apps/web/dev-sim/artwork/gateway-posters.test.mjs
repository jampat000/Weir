// @vitest-environment node
import { mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  GATEWAY_URL,
  MAX_LOOKUPS,
  loadGatewayPosters,
} from "./gateway-posters.mjs";

const SINTEL = {
  id: "movie-sintel-2010",
  mediaType: "movie",
  title: "Sintel",
  year: 2010,
};
const DETOUR = {
  id: "movie-detour-1945",
  mediaType: "movie",
  title: "Detour",
  year: 1945,
};
const IMAGE_URL = "https://image.tmdb.org/t/p/w500/sintel.jpg";
const IMAGE_BYTES = Uint8Array.from([255, 216, 255]);

let cacheDir;
beforeEach(async () => {
  cacheDir = await mkdtemp(join(tmpdir(), "weir-sim-posters-"));
});
afterEach(async () => {
  await rm(cacheDir, { recursive: true, force: true });
});

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
const jpeg = () =>
  new Response(IMAGE_BYTES, { headers: { "content-type": "image/jpeg" } });

/** A metadata service that knows Sintel and nothing else. */
function service() {
  return vi.fn(async (url) => {
    const { pathname, searchParams } = new URL(url);
    if (pathname === "/metadata/search") {
      return searchParams.get("query") === "Sintel"
        ? json({
            results: [{ title: "Sintel", year: 2010, posterUrl: IMAGE_URL }],
          })
        : json({ results: [] });
    }
    return jpeg();
  });
}

async function load(titles, options = {}) {
  const found = new Map();
  const log = vi.fn();
  await loadGatewayPosters(titles, {
    cacheDir,
    onPoster: (id, image) => found.set(id, image),
    log,
    pause: async () => {},
    gatewayUrl: GATEWAY_URL,
    ...options,
  });
  return { found, log };
}

describe("looking posters up in the metadata service", () => {
  it("fetches the 342px poster of the title that matches by name and year", async () => {
    const fetchImpl = service();

    const { found } = await load([SINTEL], { fetchImpl });

    expect(found.get(SINTEL.id)?.contentType).toBe("image/jpeg");
    expect(fetchImpl.mock.calls.map(([url]) => url)).toContain(
      "https://image.tmdb.org/t/p/w342/sintel.jpg",
    );
  });

  it("does not take a result for another year", async () => {
    const fetchImpl = vi.fn(async () =>
      json({
        results: [{ title: "Sintel", year: 2011, posterUrl: IMAGE_URL }],
      }),
    );

    const { found } = await load([SINTEL], { fetchImpl });

    expect(found.size).toBe(0);
  });

  it("does not fetch a poster from a host it does not know", async () => {
    const fetchImpl = vi.fn(async () =>
      json({
        results: [
          {
            title: "Sintel",
            year: 2010,
            posterUrl: "https://elsewhere.example/p.jpg",
          },
        ],
      }),
    );

    const { found } = await load([SINTEL], { fetchImpl });

    expect(found.size).toBe(0);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("asks nothing the second time, whether it found a poster or not", async () => {
    await load([SINTEL, DETOUR], { fetchImpl: service() });
    const second = service();

    const { found } = await load([SINTEL, DETOUR], { fetchImpl: second });

    expect(second).not.toHaveBeenCalled();
    expect(found.get(SINTEL.id)?.body.length).toBe(IMAGE_BYTES.length);
    expect(await readdir(cacheDir)).toEqual(
      expect.arrayContaining([`${SINTEL.id}.jpg`, `${DETOUR.id}.none`]),
    );
  });

  it("only waits between lookups it actually makes", async () => {
    await writeFile(join(cacheDir, `${SINTEL.id}.jpg`), IMAGE_BYTES);
    const pause = vi.fn(async () => {});

    await load([SINTEL, DETOUR], { fetchImpl: service(), pause });

    expect(pause).not.toHaveBeenCalled();
  });

  it("stops asking for the rest once the service cannot be reached, and keeps nothing for the failure", async () => {
    const fetchImpl = vi.fn(async () => {
      throw new TypeError("fetch failed");
    });

    const { log } = await load([SINTEL, DETOUR], { fetchImpl });

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    expect(log).toHaveBeenCalledWith(
      expect.stringContaining("cannot be reached"),
    );
    expect(await readdir(cacheDir)).toEqual([]);
  });

  it("stops when the service says it is busy", async () => {
    const fetchImpl = vi.fn(async () => json({ error: "provider_busy" }, 503));

    await load([SINTEL, DETOUR], { fetchImpl });

    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("makes no more lookups than its limit in one start-up", async () => {
    const titles = Array.from({ length: MAX_LOOKUPS + 5 }, (_, index) => ({
      id: `movie-title-${index}-2000`,
      mediaType: "movie",
      title: `Title ${index}`,
      year: 2000,
    }));
    const fetchImpl = vi.fn(async () => json({ results: [] }));

    await load(titles, { fetchImpl });

    expect(fetchImpl).toHaveBeenCalledTimes(MAX_LOOKUPS);
  });
});
