// @vitest-environment node
import { describe, expect, it } from "vitest";

import { findOperation, successResponse } from "../openapi/spec.mjs";
import { violations } from "../openapi/validate.mjs";
import { SCENARIOS } from "../scenarios.mjs";
import { SECOND_MS } from "../wire-time.mjs";
import { ask, createTestSim } from "../test-support.mjs";

const PATH = "/api/v1/suite/network-access";
const ASKED_AND_ANSWERED_MS = 5 * SECOND_MS;

const read = (sim) => ask(sim, "GET", PATH).body;
const choose = (sim, scope) => ask(sim, "PUT", PATH, { scope });

describe("the simulated network access", () => {
  it("starts with only this PC able to reach Weir, and answers in the contract's shape", () => {
    const { sim } = createTestSim();

    const body = read(sim);

    expect(body).toMatchObject({
      state: "this_pc_only",
      summary: "Only this PC can reach Weir.",
      scope: "this_pc_only",
      pending_scope: null,
      firewall: "not_checked",
      addresses: [],
    });
    expect(
      violations(successResponse(findOperation("GET", PATH)).schema, body),
    ).toEqual([]);
  });

  it("answers a choice for the network at once with the change pending and waiting for approval on this PC", () => {
    const { sim } = createTestSim();

    const { status, body } = choose(sim, "network");

    expect(status).toBe(200);
    expect(body).toMatchObject({
      state: "this_pc_only",
      pending_scope: "network",
      firewall: "blocked",
      addresses: ["http://10.0.0.196:9347"],
    });
    expect(body.summary).toBe(
      "Waiting for you to approve Windows Firewall on MEDIA-PC. Weir restarts for your network once you do.",
    );
  });

  it("allows the network a few seconds after the choice is made", () => {
    const { sim, advance } = createTestSim();
    choose(sim, "network");

    advance(ASKED_AND_ANSWERED_MS);
    const body = read(sim);

    expect(body).toMatchObject({
      state: "allowed",
      scope: "network",
      pending_scope: null,
      firewall: "allowed",
    });
    expect(body.summary).toBe("Other devices on your network can reach Weir.");
  });

  it("goes back to this PC only when asked, and says Weir is restarting until then", () => {
    const { sim, advance } = createTestSim();
    choose(sim, "network");
    advance(ASKED_AND_ANSWERED_MS);

    const pending = choose(sim, "this_pc_only").body;
    advance(ASKED_AND_ANSWERED_MS);

    expect(pending.summary).toBe(
      "Restarting Weir so only this PC can reach it.",
    );
    expect(read(sim)).toMatchObject({
      state: "this_pc_only",
      scope: "this_pc_only",
      firewall: "not_checked",
    });
  });

  it("restarts without asking again once Windows has allowed Weir", () => {
    const { sim, advance } = createTestSim();
    choose(sim, "network");
    advance(ASKED_AND_ANSWERED_MS);
    choose(sim, "this_pc_only");
    advance(ASKED_AND_ANSWERED_MS);

    const body = choose(sim, "network").body;

    expect(body.summary).toBe(
      "Restarting Weir so other devices on your network can reach it.",
    );
  });

  it("refuses a scope that is not one of the two choices", () => {
    const { sim } = createTestSim();

    expect(choose(sim, "everyone").status).toBe(422);
    expect(read(sim).scope).toBe("this_pc_only");
  });

  it("has the trouble scenario's first prompt declined, so the firewall blocks until the person tries again", () => {
    const { sim, advance } = createTestSim({ scenario: SCENARIOS.trouble });
    choose(sim, "network");
    advance(ASKED_AND_ANSWERED_MS);

    const blocked = read(sim);
    const retry = choose(sim, "network").body;
    advance(ASKED_AND_ANSWERED_MS);

    expect(blocked).toMatchObject({
      state: "blocked",
      scope: "network",
      pending_scope: null,
      firewall: "blocked",
    });
    expect(blocked.summary).toMatch(
      /^Windows Firewall is blocking other devices/,
    );
    expect(retry.state).toBe("blocked");
    expect(read(sim).state).toBe("allowed");
  });
});
