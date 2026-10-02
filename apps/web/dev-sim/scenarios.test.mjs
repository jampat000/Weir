// @vitest-environment node
import { describe, expect, it } from "vitest";

import { STATUS } from "./engine/file.mjs";
import { operations, successResponse } from "./openapi/spec.mjs";
import { violations } from "./openapi/validate.mjs";
import { SCENARIO, SCENARIOS, scenarioNamed } from "./scenarios.mjs";
import { ask, createTestSim } from "./test-support.mjs";
import { SECOND_MS } from "./wire-time.mjs";

const filesOf = (sim, status) =>
  [...sim.engine.files.values()].filter((file) => file.status === status);

/** Files held with nothing counting down: they wait for a person, not for time. */
const stuckOnHold = (sim) =>
  filesOf(sim, STATUS.ON_HOLD).filter((file) => file.holdUntil === null);

const rejectedForLanguage = (sim) =>
  filesOf(sim, STATUS.REJECTED).filter((file) =>
    file.statusReason.includes("None of its audio tracks are in English"),
  );

const sessionOf = (name) =>
  createTestSim({ withHistory: true, scenario: SCENARIOS[name] });

describe("choosing a scenario", () => {
  it("is busy when none is asked for", () => {
    expect(scenarioNamed(undefined).name).toBe(SCENARIO.BUSY);
    expect(scenarioNamed("").name).toBe(SCENARIO.BUSY);
  });

  it("takes the name in any case and without stray spaces", () => {
    expect(scenarioNamed(" Quiet ").name).toBe(SCENARIO.QUIET);
    expect(scenarioNamed("TROUBLE").name).toBe(SCENARIO.TROUBLE);
  });

  it("refuses a name it does not know, and says which it does", () => {
    expect(() => scenarioNamed("chaos")).toThrow(
      "SIM_SCENARIO must be one of busy, quiet, trouble",
    );
  });
});

describe("every scenario", () => {
  const shapeProblems = (sim, template, path) => {
    const operation = operations().find(
      (candidate) =>
        candidate.method === "GET" && candidate.template === template,
    );
    const reply = ask(sim, "GET", path);
    return reply.status === 200
      ? violations(successResponse(operation).schema, reply.body, template)
      : [`${template} answered ${reply.status}`];
  };

  it.each(Object.values(SCENARIO))(
    "answers the folder chains, connections and files in the shapes the contract gives in the %s scenario",
    (name) => {
      const { sim } = sessionOf(name);
      const chains = sim.store.libraries.flatMap((library) =>
        shapeProblems(
          sim,
          "/api/v1/processing/libraries/{library_id}/folder-chain",
          `/api/v1/processing/libraries/${library.id}/folder-chain`,
        ),
      );

      expect([
        ...chains,
        ...shapeProblems(
          sim,
          "/api/v1/media-managers/connections",
          "/api/v1/media-managers/connections",
        ),
        ...shapeProblems(
          sim,
          "/api/v1/media-managers/capabilities",
          "/api/v1/media-managers/capabilities",
        ),
        ...shapeProblems(
          sim,
          "/api/v1/processing/libraries",
          "/api/v1/processing/libraries",
        ),
        ...shapeProblems(
          sim,
          "/api/v1/processing/files",
          "/api/v1/processing/files",
        ),
      ]).toEqual([]);
    },
  );
});

describe("the busy scenario", () => {
  it("has a file waiting on a person for each reason: language, a failed write, a held file and a path rule", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    expect(rejectedForLanguage(sim)).toHaveLength(3);
    expect(filesOf(sim, STATUS.FAILED)).toHaveLength(1);
    expect(stuckOnHold(sim)).toHaveLength(1);
    const [skipped, ...others] = filesOf(sim, STATUS.SKIPPED);
    expect(others).toEqual([]);
    expect(skipped.statusReason).toBe(
      "Skipped because its path matches this workflow's exclude patterns.",
    );
  });

  it("spreads the files over all four workflows", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const names = new Set(
      [...sim.engine.files.values()].map((file) => file.libraryName),
    );

    expect(names).toEqual(new Set(["Movies", "TV", "Kids", "4K Movies"]));
  });

  it("keeps the work in progress on the board", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    expect(filesOf(sim, STATUS.PROCESSING).length).toBeGreaterThanOrEqual(2);
    expect(filesOf(sim, STATUS.WAITING).length).toBeGreaterThan(0);
  });
});

describe("the quiet scenario", () => {
  it("opens with a little finished work and nothing waiting, held or in need of a person", () => {
    const { sim } = sessionOf(SCENARIO.QUIET);

    const statuses = new Set(
      [...sim.engine.files.values()].map((file) => file.status),
    );

    expect(statuses).toEqual(new Set([STATUS.PROCESSED]));
    expect(sim.engine.files.size).toBeLessThan(10);
  });

  it("lets a minute go by without a new download turning up", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);
    const before = sim.engine.files.size;

    advance(60 * SECOND_MS);

    expect(sim.engine.files.size).toBe(before);
  });

  it("has every workflow in sync and every connection answering", () => {
    const { sim } = sessionOf(SCENARIO.QUIET);

    const chains = sim.store.libraries.map(
      (library) =>
        ask(
          sim,
          "GET",
          `/api/v1/processing/libraries/${library.id}/folder-chain`,
        ).body,
    );

    for (const chain of chains) {
      const states = [
        ...chain.local.lines,
        ...chain.managers.flatMap((manager) => manager.lines),
        ...chain.download_clients.flatMap((client) => client.lines),
      ].map((line) => line.state);
      expect(chain.ready).toBe(true);
      expect(states).not.toContain("unverified");
    }
    expect(
      [...sim.store.managers, ...sim.store.downloadClients].map(
        (connection) => connection.last_test_ok,
      ),
    ).not.toContain(false);
  });
});

describe("the trouble scenario", () => {
  it("has many files waiting on a person, for every reason", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    expect(rejectedForLanguage(sim)).toHaveLength(7);
    expect(filesOf(sim, STATUS.FAILED)).toHaveLength(3);
    expect(stuckOnHold(sim)).toHaveLength(2);
    expect(filesOf(sim, STATUS.SKIPPED)).toHaveLength(2);
  });

  it("has Sonarr not answering from the start, so the TV workflow needs a fix", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    const sonarr = sim.store.managers.find(
      (manager) => manager.kind === "sonarr",
    );
    const chain = ask(
      sim,
      "GET",
      "/api/v1/processing/libraries/2/folder-chain",
    ).body;

    expect(sonarr.last_test_ok).toBe(false);
    expect(chain.ready).toBe(false);
    expect(chain.managers[0].lines).toEqual([
      expect.objectContaining({
        state: "problem",
        text: expect.stringContaining("Weir could not reach Sonarr"),
      }),
    ]);
  });

  it("keeps Sonarr down however long the session runs", () => {
    const { sim, advance } = sessionOf(SCENARIO.TROUBLE);

    advance(30 * 60 * SECOND_MS);

    expect(
      sim.store.managers.find((manager) => manager.kind === "sonarr")
        .last_test_ok,
    ).toBe(false);
  });
});

describe("what a person can do about the files that wait on them", () => {
  it("queues the files rejected for language again with Process all again", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    const reply = ask(
      sim,
      "POST",
      "/api/v1/processing/files/rejected/process-again",
    );

    expect(reply.body.requeued).toBe(3);
    expect(filesOf(sim, STATUS.REJECTED)).toEqual([]);
  });

  it("queues a failed file again with Try again", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);
    const [failed] = filesOf(sim, STATUS.FAILED);

    const reply = ask(
      sim,
      "POST",
      `/api/v1/processing/files/${failed.id}/requeue`,
    );

    expect(reply.body.requeued).toBe(1);
    expect(failed.status).toBe(STATUS.WAITING);
  });

  it("offers the remove options for a held file and keeps it listed as kept when asked to", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);
    const [held] = stuckOnHold(sim);

    const options = ask(
      sim,
      "GET",
      `/api/v1/processing/files/${held.id}/remove-options`,
    );
    ask(sim, "DELETE", `/api/v1/processing/files/${held.id}`, {
      resolution: "keep",
    });

    expect(options.status).toBe(200);
    expect(sim.engine.files.has(held.id)).toBe(false);
    expect(
      ask(sim, "GET", "/api/v1/processing/kept-files").body.files.map(
        (file) => file.id,
      ),
    ).toContain(held.id);
  });

  it("lets a file held by a path rule be removed from Activity", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);
    const [skipped] = filesOf(sim, STATUS.SKIPPED);

    const reply = ask(sim, "DELETE", `/api/v1/processing/files/${skipped.id}`, {
      resolution: "delete",
    });

    expect(reply.body.done).toBe(true);
    expect(sim.engine.files.has(skipped.id)).toBe(false);
  });
});
