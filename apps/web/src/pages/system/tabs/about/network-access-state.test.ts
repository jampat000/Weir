import { describe, expect, it } from "vitest";

import type { NetworkAccessStatus } from "../../../../lib/settings/types";
import { describeNetworkAccess, intendedScope } from "./network-access-state";

function status(over: Partial<NetworkAccessStatus> = {}): NetworkAccessStatus {
  return {
    state: "this_pc_only",
    summary: "",
    scope: "this_pc_only",
    pending_scope: null,
    firewall: "not_checked",
    port: 9347,
    machine_name: "MEDIA-PC",
    addresses: [],
    ...over,
  };
}

describe("intendedScope", () => {
  it("is what the running server does when nothing is pending", () => {
    expect(intendedScope(status({ scope: "network" }))).toBe("network");
  });

  it("is the pending choice while the tray has not carried it out", () => {
    expect(intendedScope(status({ pending_scope: "network" }))).toBe("network");
  });

  it("falls back to this PC only where the server does not say", () => {
    expect(intendedScope(status({ scope: null }))).toBe("this_pc_only");
  });
});

describe("describeNetworkAccess", () => {
  it("offers no retry and no address while only this PC can reach Weir", () => {
    expect(describeNetworkAccess(status())).toMatchObject({
      meaning: "idle",
      label: "This PC only",
      addresses: [],
      canRetry: false,
    });
  });

  it("names the addresses when Weir is reachable", () => {
    const line = describeNetworkAccess(
      status({
        state: "allowed",
        scope: "network",
        addresses: ["http://10.0.0.196:9347"],
      }),
    );

    expect(line).toMatchObject({
      meaning: "done",
      addresses: ["http://10.0.0.196:9347"],
    });
  });

  it("offers a retry when the firewall blocks", () => {
    expect(
      describeNetworkAccess(status({ state: "blocked", scope: "network" })),
    ).toMatchObject({ meaning: "broken", canRetry: true });
  });

  it("waits for approval while the firewall still has to be asked", () => {
    const line = describeNetworkAccess(
      status({ pending_scope: "network", firewall: "blocked" }),
    );

    expect(line.label).toBe("Waiting for approval on MEDIA-PC");
    expect(line.meaning).toBe("attention");
    expect(line.note).not.toBeNull();
  });

  it("only restarts when the firewall already allows it", () => {
    expect(
      describeNetworkAccess(
        status({ pending_scope: "network", firewall: "allowed" }),
      ),
    ).toMatchObject({
      label: "Restarting Weir for your network…",
      meaning: "doing",
    });
  });

  it("restarts for this PC only when that is what is pending", () => {
    expect(
      describeNetworkAccess(
        status({
          state: "allowed",
          scope: "network",
          pending_scope: "this_pc_only",
        }),
      ).label,
    ).toBe("Restarting Weir for this PC only…");
  });
});
