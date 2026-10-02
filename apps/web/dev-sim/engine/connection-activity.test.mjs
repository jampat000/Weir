// @vitest-environment node
import { describe, expect, it } from "vitest";

import { ask, createTestSim } from "../test-support.mjs";
import { FOUR_K_MANAGER_ID } from "../fixtures/connections.mjs";
import { SCENARIOS } from "../scenarios.mjs";
import { fromWire, SECOND_MS } from "../wire-time.mjs";
import { STATUS } from "./file.mjs";
import { VERDICT } from "./plan.mjs";

const MOVIES_LIBRARY = 1;
const MOVIES_MANAGER = 1;
const QBITTORRENT = 1;

function session(options = {}) {
  const test = createTestSim({ withHistory: true, ...options });
  const frames = [];
  test.sim.engine.connections.activity.onFrame((frame) => frames.push(frame));
  return { ...test, frames };
}

const framesOf = (frames, kind, id) =>
  frames.filter((frame) => frame.kind === kind && frame.id === id);

const testManager = (sim, id) =>
  ask(sim, "POST", `/api/v1/media-managers/connections/${id}/test`).body;

describe("a test of a connection", () => {
  it("says it asked, then that it answered with how long it took", () => {
    const { sim, frames, advance } = session();

    testManager(sim, MOVIES_MANAGER);
    advance(SECOND_MS);

    const [asked, answered] = framesOf(frames, "media_manager", MOVIES_MANAGER);
    expect(asked).toMatchObject({
      phase: "asked",
      direction: "outbound",
      ms: null,
    });
    expect(answered).toMatchObject({
      phase: "answered",
      direction: "outbound",
    });
    expect(answered.ms).toBeGreaterThan(0);
    expect(answered.ms).toBeLessThan(2000);
  });

  it("writes how long it took and when on the connection", () => {
    const { sim, advance } = session();

    testManager(sim, MOVIES_MANAGER);
    advance(SECOND_MS);

    const manager = sim.store.managers.find(({ id }) => id === MOVIES_MANAGER);
    expect(manager.last_answer_ms).toBeGreaterThan(0);
    expect(fromWire(manager.last_used_at)).toBeGreaterThanOrEqual(
      fromWire(manager.last_test_at),
    );
  });

  it("fails, taking its time, while the flaky 4K manager is not answering", () => {
    const { sim, frames, advance } = session();

    advance(170 * SECOND_MS);
    testManager(sim, FOUR_K_MANAGER_ID);
    advance(2 * SECOND_MS);

    const last = framesOf(frames, "media_manager", FOUR_K_MANAGER_ID).at(-1);
    expect(last).toMatchObject({ phase: "failed", direction: "outbound" });
    expect(last.ms).toBeGreaterThan(0);
  });

  it("answers slowly while the flaky 4K manager is slow", () => {
    const { sim, advance } = session();

    advance(80 * SECOND_MS);
    testManager(sim, FOUR_K_MANAGER_ID);
    advance(5 * SECOND_MS);

    const manager = sim.store.managers.find(
      ({ id }) => id === FOUR_K_MANAGER_ID,
    );
    expect(manager.last_test_ok).toBe(true);
    expect(manager.last_answer_ms).toBeGreaterThanOrEqual(2000);
  });
});

describe("what lights the connections without anyone testing them", () => {
  it("has the media manager asked when a file is handed back, and calling back when it takes the copy", () => {
    const { sim, clock, frames, advance } = session({
      scenario: SCENARIOS.quiet,
    });
    const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
      verdict: VERDICT.CLEAN,
    });
    Object.assign(file, { status: STATUS.WAITING, holdUntil: null });

    advance(3 * 60 * SECOND_MS);

    const own = framesOf(frames, "media_manager", MOVIES_MANAGER);
    const handedBack = own.findIndex(
      ({ phase, direction }) =>
        phase === "answered" && direction === "outbound",
    );
    const calledBack = own.findIndex(
      ({ direction }) => direction === "inbound",
    );
    expect(handedBack).toBeGreaterThanOrEqual(0);
    expect(calledBack).toBeGreaterThan(handedBack);
    expect(own[calledBack]).toMatchObject({ phase: "answered", ms: null });
  });

  it("has a folder-chain check ask each manager and download client the chain names", () => {
    const { sim, frames } = session();

    ask(
      sim,
      "GET",
      `/api/v1/processing/libraries/${MOVIES_LIBRARY}/folder-chain`,
    );

    const asked = frames.filter(({ phase }) => phase === "asked");
    expect(asked.map(({ kind, id }) => `${kind}:${id}`).sort()).toEqual([
      `download_client:${QBITTORRENT}`,
      `media_manager:${MOVIES_MANAGER}`,
    ]);
  });
});

describe("the connections the API lists", () => {
  it("say how long the last call took and when Weir last talked to each", () => {
    const { sim } = session();

    const listed = [
      ...ask(sim, "GET", "/api/v1/media-managers/connections").body,
      ...ask(sim, "GET", "/api/v1/download-clients/connections").body,
    ];

    expect(listed.length).toBeGreaterThan(0);
    for (const connection of listed) {
      expect(connection.last_answer_ms).toBeGreaterThan(0);
      expect(fromWire(connection.last_used_at)).not.toBeNull();
    }
  });
});
