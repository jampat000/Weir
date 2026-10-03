// @vitest-environment node
import { describe, expect, it } from "vitest";

import { SCENARIOS } from "../scenarios.mjs";
import { ask, createTestSim } from "../test-support.mjs";
import { fromWire, MINUTE_MS, SECOND_MS } from "../wire-time.mjs";

const MOVIES = 1;
const TV = 2;
const KIDS = 3;
const FOUR_K = 4;
const FOUR_K_MANAGER = 3;

const chainOf = (sim, libraryId) =>
  ask(sim, "GET", `/api/v1/processing/libraries/${libraryId}/folder-chain`)
    .body;

const linesOf = (chain) => [
  ...chain.local.lines,
  ...chain.managers.flatMap((manager) => manager.lines),
  ...chain.download_clients.flatMap((client) => client.lines),
];

const statesOf = (chain) => linesOf(chain).map((line) => line.state);

const session = (options = {}) =>
  createTestSim({ withHistory: true, ...options });

describe("the four workflows", () => {
  it("lists Movies, TV, Kids and 4K Movies", () => {
    const { sim } = session();

    const libraries = ask(sim, "GET", "/api/v1/processing/libraries").body;

    expect(
      libraries.map((library) => [library.name, library.media_type]),
    ).toEqual([
      ["Movies", "movie"],
      ["TV", "tv"],
      ["Kids", "movie"],
      ["4K Movies", "movie"],
    ]);
  });

  it("links Kids to no manager and 4K Movies to a second Radarr of its own", () => {
    const { sim } = session();

    const libraries = ask(sim, "GET", "/api/v1/processing/libraries").body;
    const radarrs = sim.store.managers.filter(
      (manager) => manager.kind === "radarr",
    );

    expect(libraries[KIDS - 1].manager_connection_ids).toEqual([]);
    expect(libraries[KIDS - 1].manager_coverage).toBe("no_upstream_signal");
    expect(libraries[FOUR_K - 1].manager_connection_ids).toEqual([
      FOUR_K_MANAGER,
    ]);
    expect(radarrs.map((manager) => manager.nickname)).toEqual([null, "4K"]);
  });

  it("gives each one files in its library for the Library page", () => {
    const { sim } = session();

    const filesIn = (libraryId) =>
      ask(sim, "GET", `/api/v1/processing/libraries/${libraryId}/library-files`)
        .body.total;

    for (const libraryId of [MOVIES, TV, KIDS, FOUR_K])
      expect(filesIn(libraryId)).toBeGreaterThan(0);
  });

  it("finishes downloads in every workflow, and hands a Kids copy back to nobody", () => {
    const { sim } = session();

    const done = (name) =>
      [...sim.engine.files.values()].filter(
        (file) => file.libraryName === name && file.status === "processed",
      );
    const cleanedKids = done("Kids").filter((file) => file.handback);

    expect(done("Movies").length).toBeGreaterThan(0);
    expect(done("TV").length).toBeGreaterThan(0);
    expect(done("Kids").length).toBeGreaterThan(0);
    expect(done("4K Movies").length).toBeGreaterThan(0);
    expect(cleanedKids.length).toBeGreaterThan(0);
    expect(cleanedKids.every((file) => file.handback.outcome === null)).toBe(
      true,
    );
  });
});

describe("the folder chain of each workflow", () => {
  it("has Movies and TV in sync, with every line read by Weir", () => {
    const { sim } = session();

    for (const libraryId of [MOVIES, TV]) {
      const chain = chainOf(sim, libraryId);
      expect(chain.ready).toBe(true);
      expect(new Set(statesOf(chain))).toEqual(new Set(["ok"]));
    }
  });

  it("has Kids in sync with Weir's own folders alone", () => {
    const { sim } = session();

    const chain = chainOf(sim, KIDS);

    expect(chain.ready).toBe(true);
    expect(chain.managers).toEqual([]);
    expect(chain.download_clients).toEqual([]);
    expect(new Set(statesOf(chain))).toEqual(new Set(["ok"]));
  });

  it("has 4K Movies ready but not verified, because its manager does not say where its downloads land", () => {
    const { sim } = session();

    const chain = chainOf(sim, FOUR_K);
    const unverified = linesOf(chain).filter(
      (line) => line.state === "unverified",
    );

    expect(chain.ready).toBe(true);
    expect(unverified).toEqual([
      expect.objectContaining({
        text: expect.stringContaining("does not say where Transmission saves"),
      }),
    ]);
  });

  it("describes a Sonarr or Radarr link as a remote path mapping the way the server does", () => {
    const { sim } = session();

    const [link] = chainOf(sim, MOVIES).managers;

    expect(link).toEqual(
      expect.objectContaining({
        flow: "remote_path_mapping",
        label: "Radarr",
        mapping: {
          hosts: ["localhost"],
          remote_path: "D:\\Downloads\\Movies",
          local_path: "D:\\Weir\\hand-back\\Movies",
        },
      }),
    );
  });

  it("reads only the download clients a workflow uses", () => {
    const { sim } = session();

    const names = (libraryId) =>
      chainOf(sim, libraryId).download_clients.map((client) => client.kind);

    expect(names(MOVIES)).toEqual(["qbittorrent"]);
    expect(names(TV)).toEqual(["sabnzbd"]);
    expect(names(KIDS)).toEqual([]);
    expect(names(FOUR_K)).toEqual([]);
  });
});

describe("what the connections say about when they last answered", () => {
  it("has every connection answering a little while before the session opened, and not all at the same age", () => {
    const { sim } = session();

    const ages = [...sim.store.managers, ...sim.store.downloadClients].map(
      (connection) => sim.now() - fromWire(connection.last_test_at),
    );

    for (const age of ages) {
      expect(age).toBeGreaterThanOrEqual(0);
      expect(age).toBeLessThan(MINUTE_MS);
    }
    expect(new Set(ages).size).toBe(ages.length);
  });

  it("asks again about every minute, so how long ago each answered keeps changing", () => {
    const { sim, advance } = session({ scenario: SCENARIOS.quiet });
    const before = sim.store.managers.map((manager) => manager.last_test_at);

    advance(2 * MINUTE_MS);

    sim.store.managers.forEach((manager, index) => {
      expect(fromWire(manager.last_test_at)).toBeGreaterThan(
        fromWire(before[index]),
      );
      expect(sim.now() - fromWire(manager.last_test_at)).toBeLessThanOrEqual(
        MINUTE_MS,
      );
    });
  });

  it("stops answering for a while in the busy scenario, then recovers, and says so in the folder chain", () => {
    const { sim, advance } = session();
    const radarr4k = () =>
      sim.store.managers.find((manager) => manager.id === FOUR_K_MANAGER);

    advance(3 * MINUTE_MS);
    const whileDown = {
      answering: radarr4k().last_test_ok,
      chainReady: chainOf(sim, FOUR_K).ready,
      detail: radarr4k().last_test_detail,
    };
    advance(3 * MINUTE_MS);

    expect(whileDown.answering).toBe(false);
    expect(whileDown.chainReady).toBe(false);
    expect(whileDown.detail).toContain("Weir could not reach Radarr (4K)");
    expect(radarr4k().last_test_ok).toBe(true);
    expect(chainOf(sim, FOUR_K).ready).toBe(true);
  });

  it("answers a test straight away with what the connection is doing now", () => {
    const { sim, advance } = session();
    const test = () =>
      ask(
        sim,
        "POST",
        `/api/v1/media-managers/connections/${FOUR_K_MANAGER}/test`,
      ).body;

    advance(200 * SECOND_MS);
    const down = test();
    advance(60 * SECOND_MS);
    const recovered = test();

    expect(down.ok).toBe(false);
    expect(recovered.ok).toBe(true);
    expect(fromWire(recovered.checked_at)).toBe(sim.now());
  });

  it("reports an unreachable manager as one Weir cannot ask anything", () => {
    const { sim } = session({ scenario: SCENARIOS.trouble });

    const sonarr = ask(sim, "GET", "/api/v1/media-managers/capabilities")
      .body.filter((manager) => manager.kind === "sonarr")
      .at(0);

    expect(sonarr.reachable).toBe(false);
    expect(sonarr.reports_import_queue).toBe(false);
  });
});

describe("System › About", () => {
  it("says the server answers only this PC with the state and words the server uses", () => {
    const { sim } = session();

    const access = ask(sim, "GET", "/api/v1/suite/network-access").body;

    expect(access.state).toBe("this_pc_only");
    expect(access.summary).toMatch(/^Only this PC can reach Weir\./);
  });
});
