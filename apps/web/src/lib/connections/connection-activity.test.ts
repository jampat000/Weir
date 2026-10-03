import { describe, expect, it } from "vitest";

import { parseConnectionActivity } from "./connection-activity";

const frame = (overrides: Record<string, unknown> = {}) =>
  JSON.stringify({
    kind: "media_manager",
    id: 3,
    phase: "answered",
    direction: "outbound",
    at: "2026-10-02T12:00:00Z",
    ms: 84,
    ...overrides,
  });

describe("a connection.activity frame", () => {
  it("is read as it was sent", () => {
    expect(parseConnectionActivity(frame())).toEqual({
      kind: "media_manager",
      id: 3,
      phase: "answered",
      direction: "outbound",
      at: "2026-10-02T12:00:00Z",
      ms: 84,
    });
  });

  it("has no time when the call has not ended", () => {
    expect(
      parseConnectionActivity(frame({ phase: "asked", ms: null }))?.ms,
    ).toBeNull();
  });

  it("reads a frame that does not say which way the call went as one Weir made", () => {
    expect(
      parseConnectionActivity(frame({ direction: undefined }))?.direction,
    ).toBe("outbound");
  });

  it.each([
    ["a kind it does not know", { kind: "indexer" }],
    ["a phase it does not know", { phase: "stalled" }],
    ["an id that is not a number", { id: "3" }],
    ["no time", { at: undefined }],
  ])("is ignored with %s", (_, overrides) => {
    expect(parseConnectionActivity(frame(overrides))).toBeNull();
  });

  it("is ignored when it is not JSON", () => {
    expect(parseConnectionActivity("not a frame")).toBeNull();
  });
});
