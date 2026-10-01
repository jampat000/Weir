import { describe, expect, it } from "vitest";

import { roundScaleTop } from "./chart-scale";

describe("the round-number top of a chart's scale", () => {
  it.each([
    [1, 1],
    [1.2, 2],
    [2, 2],
    [2.1, 2.5],
    [3, 5],
    [5, 5],
    [7, 10],
    [10, 10],
    [11, 20],
    [26, 50],
    [180, 200],
    [1000, 1000],
    [0.3, 0.5],
    [0.07, 0.1],
  ])("takes %s up to %s", (highest, top) => {
    expect(roundScaleTop(highest)).toBeCloseTo(top, 10);
  });

  it("falls back to 1 when there is nothing to show", () => {
    expect(roundScaleTop(0)).toBe(1);
    expect(roundScaleTop(-4)).toBe(1);
    expect(roundScaleTop(Number.NaN)).toBe(1);
  });
});
