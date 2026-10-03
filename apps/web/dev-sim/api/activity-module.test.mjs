// @vitest-environment node
import { describe, expect, it } from "vitest";

import { EVENT_TYPE } from "../engine/records.mjs";
import { ask, createTestSim } from "../test-support.mjs";

function simWithEachKindOfEntry() {
  const { sim, clock } = createTestSim();
  const at = clock.now();
  const record = (type) =>
    sim.engine.activity.record({ type, title: type }, at);
  record(EVENT_TYPE.PASS_COMPLETED);
  record(EVENT_TYPE.LIBRARY_FILE_CLEANED);
  record("auth.login_succeeded");
  return sim;
}

const typesFor = (sim, module) =>
  ask(sim, "GET", `/api/v1/activity/recent?module=${module}`).body.items.map(
    (item) => item.event_type,
  );

describe("narrowing the simulated Activity feed to a module", () => {
  it("keeps a library's entries apart from a new download's, as the server does", () => {
    const sim = simWithEachKindOfEntry();

    expect(typesFor(sim, "processing")).toEqual([EVENT_TYPE.PASS_COMPLETED]);
    expect(typesFor(sim, "library")).toEqual([EVENT_TYPE.LIBRARY_FILE_CLEANED]);
  });

  it("reads system as every module but processing", () => {
    const sim = simWithEachKindOfEntry();

    expect(typesFor(sim, "system").sort()).toEqual([
      "auth.login_succeeded",
      EVENT_TYPE.LIBRARY_FILE_CLEANED,
    ]);
  });

  it("tells each entry which module it is in", () => {
    const sim = simWithEachKindOfEntry();

    const modules = ask(sim, "GET", "/api/v1/activity/recent").body.items.map(
      (item) => item.module,
    );

    expect(modules.sort()).toEqual(["auth", "library", "processing"]);
  });
});
