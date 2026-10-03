import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { WithWorkflows, workflow } from "../../test/with-workflows";
import {
  UNKNOWN_WORKFLOW_HUE,
  WORKFLOW_HUES,
  hueAtPosition,
  inDisplayOrder,
  useWorkflowHues,
  workflowHuesOf,
} from "./workflow-hues";

/** Eight workflows, saved in an order other than the one Settings lists them in. */
const EIGHT = [
  workflow(11, "Movies", 0),
  workflow(12, "TV", 1),
  workflow(13, "Kids", 2),
  workflow(14, "4K Movies", 3),
  workflow(15, "Anime", 4),
  workflow(16, "Docs", 5),
  workflow(17, "Shorts", 6),
  workflow(18, "Concerts", 7),
];

describe("the palette", () => {
  it("is Deluno's, in its order", () => {
    expect(WORKFLOW_HUES).toEqual([205, 265, 320, 188, 232, 290]);
  });

  it("is taken in order, and goes round again after the sixth", () => {
    expect([0, 1, 2, 3, 4, 5].map(hueAtPosition)).toEqual([
      205, 265, 320, 188, 232, 290,
    ]);
    expect(hueAtPosition(6)).toBe(205);
    expect(hueAtPosition(7)).toBe(265);
    expect(hueAtPosition(13)).toBe(265);
  });
});

describe("a workflow's hue", () => {
  it("comes from its place in Settings, by id or by name", () => {
    const hues = workflowHuesOf(EIGHT);

    expect(hues.forId(11)).toBe(205);
    expect(hues.forName("TV")).toBe(265);
    expect(hues.forId(13)).toBe(320);
    expect(hues.forName("4K Movies")).toBe(188);
  });

  it("wraps after the sixth workflow", () => {
    const hues = workflowHuesOf(EIGHT);

    expect(hues.forName("Shorts")).toBe(205);
    expect(hues.forId(18)).toBe(265);
  });

  it("goes by display order, not by the order the list came back in", () => {
    const hues = workflowHuesOf([
      workflow(2, "TV", 1),
      workflow(1, "Movies", 0),
    ]);

    expect(hues.forName("Movies")).toBe(WORKFLOW_HUES[0]);
    expect(hues.forName("TV")).toBe(WORKFLOW_HUES[1]);
  });

  it("is the neutral hue for a workflow that is not in the list, or none at all", () => {
    const hues = workflowHuesOf(EIGHT);

    expect(hues.forId(99)).toBe(UNKNOWN_WORKFLOW_HUE);
    expect(hues.forId(null)).toBe(UNKNOWN_WORKFLOW_HUE);
    expect(hues.forId(undefined)).toBe(UNKNOWN_WORKFLOW_HUE);
    expect(hues.forName("Deleted")).toBe(UNKNOWN_WORKFLOW_HUE);
    expect(hues.forName(undefined)).toBe(UNKNOWN_WORKFLOW_HUE);
    expect(workflowHuesOf([]).forName("Movies")).toBe(UNKNOWN_WORKFLOW_HUE);
  });

  it("takes the neutral hue from the palette's first", () => {
    expect(UNKNOWN_WORKFLOW_HUE).toBe(WORKFLOW_HUES[0]);
  });
});

describe("the order Settings lists workflows in", () => {
  it("is by display order, leaving the list it was given as it was", () => {
    const given = [workflow(2, "TV", 1), workflow(1, "Movies", 0)];

    expect(inDisplayOrder(given).map((one) => one.name)).toEqual([
      "Movies",
      "TV",
    ]);
    expect(given.map((one) => one.name)).toEqual(["TV", "Movies"]);
  });
});

describe("useWorkflowHues", () => {
  it("reads the workflows already loaded", () => {
    const { result } = renderHook(() => useWorkflowHues(), {
      wrapper: ({ children }) => (
        <WithWorkflows workflows={EIGHT}>{children}</WithWorkflows>
      ),
    });

    expect(result.current.forName("Kids")).toBe(320);
    expect(result.current.forId(14)).toBe(188);
  });
});
