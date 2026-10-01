// @vitest-environment node
import { describe, expect, it } from "vitest";

import { EVENT_TYPE } from "../engine/records.mjs";
import { VERDICT } from "../engine/plan.mjs";
import { ask, createTestSim } from "../test-support.mjs";

function simWithOneFile() {
  const { sim, clock } = createTestSim();
  const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
    verdict: VERDICT.CLEAN,
  });
  return { sim, file };
}

describe("the simulated artwork", () => {
  it("puts the address of a title's poster on its file", () => {
    const { sim, file } = simWithOneFile();

    const { body } = ask(sim, "GET", "/api/v1/processing/files");

    expect(body.files[0].poster_url).toBe(
      `/api/v1/artwork/posters/${file.posterId}`,
    );
  });

  it("serves the picture at that address", () => {
    const { sim, file } = simWithOneFile();

    const reply = ask(sim, "GET", `/api/v1/artwork/posters/${file.posterId}`);

    expect(reply.status).toBe(200);
    expect(reply.contentType).toMatch(/^image\//);
  });

  it("refuses an address for a title it has no poster for", () => {
    const { sim } = simWithOneFile();

    const reply = ask(sim, "GET", "/api/v1/artwork/posters/movie-nothing-1999");

    expect(reply.status).toBe(404);
  });

  it("takes every address away once artwork is switched off", () => {
    const { sim } = simWithOneFile();
    sim.store.metadataProvider.artwork_enabled = false;

    const { body } = ask(sim, "GET", "/api/v1/processing/files");

    expect(body.files[0].poster_url).toBeNull();
  });

  it("puts the address on the entries a finished file wrote", () => {
    const { sim, file } = simWithOneFile();
    sim.engine.conclude(file, sim.now() + 60_000);

    const { body } = ask(
      sim,
      "GET",
      `/api/v1/activity/recent?event_type=${EVENT_TYPE.PASS_COMPLETED}`,
    );

    expect(body.items.map((item) => item.poster_url)).toContain(
      `/api/v1/artwork/posters/${file.posterId}`,
    );
  });

  it("puts the address on a library file and keeps the simulation's own id off the wire", () => {
    const { sim } = createTestSim();
    const library = sim.store.libraries[0];

    const { body } = ask(
      sim,
      "GET",
      `/api/v1/processing/libraries/${library.id}/library-files`,
    );

    expect(body.files[0].poster_url).toMatch(/^\/api\/v1\/artwork\/posters\//);
    expect(body.files[0]).not.toHaveProperty("poster_id");
  });
});
