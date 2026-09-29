import { describe, expect, it } from "vitest";
import { FLOW_DONE, linkState, stepStates } from "./stage-flow-model";

describe("stepStates", () => {
  it("has the first step in progress and the rest up next when a file is being checked", () => {
    expect(stepStates("checking", null)).toEqual([
      "now",
      "next",
      "next",
      "next",
      "next",
    ]);
  });

  it("ticks every step before the current one", () => {
    expect(stepStates("write", null)).toEqual([
      "done",
      "done",
      "now",
      "next",
      "next",
    ]);
  });

  it("has only the last step left on the final one", () => {
    expect(stepStates("hand-back", null)).toEqual([
      "done",
      "done",
      "done",
      "done",
      "now",
    ]);
  });

  it("ticks every step once the file is done", () => {
    expect(stepStates(FLOW_DONE, null)).toEqual(Array(5).fill("done"));
  });

  it("marks the step a file failed at and ticks the ones before it", () => {
    expect(
      stepStates("write", { at: "verify", reason: "It did not play." }),
    ).toEqual(["done", "done", "done", "failed", "next"]);
  });

  it("shows a rejection on the first two steps", () => {
    expect(
      stepStates("checking", { at: "plan", reason: "Nothing to change." }),
    ).toEqual(["done", "failed", "next", "next", "next"]);
  });
});

describe("linkState", () => {
  it("fills the line into a step that is done or failed, and empties one into a step still to come", () => {
    expect(linkState("done", "plan", null)).toBe("full");
    expect(linkState("failed", "verify", null)).toBe("full");
    expect(linkState("next", "hand-back", null)).toBe("empty");
  });

  it("follows the live percent into Write", () => {
    expect(linkState("now", "write", 42)).toBe("live");
  });

  it("sweeps the line into any current step with no percent to show", () => {
    expect(linkState("now", "plan", null)).toBe("busy");
    expect(linkState("now", "verify", null)).toBe("busy");
    expect(linkState("now", "write", null)).toBe("busy");
  });
});
