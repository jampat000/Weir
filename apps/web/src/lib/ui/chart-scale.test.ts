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

describe("the top of a chart that counts whole things", () => {
  it.each([
    [0, 2],
    [1, 2],
    [2, 2],
    [3, 10],
    [5, 10],
    [10, 10],
    [11, 20],
    [26, 50],
    [51, 100],
    [180, 200],
  ])("takes %s up to %s", (highest, top) => {
    expect(roundScaleTop(highest, { wholeNumbers: true })).toBe(top);
  });

  it("always has a whole half line", () => {
    for (let highest = 0; highest <= 1_200; highest += 1) {
      const top = roundScaleTop(highest, { wholeNumbers: true });

      expect(Number.isInteger(top / 2)).toBe(true);
      expect(top).toBeGreaterThanOrEqual(highest);
    }
  });
});
