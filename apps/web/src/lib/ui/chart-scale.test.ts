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
    [0, 4],
    [1, 4],
    [4, 4],
    [5, 8],
    [8, 8],
    [9, 20],
    [20, 20],
    [21, 40],
    [40, 40],
    [41, 100],
    [100, 100],
    [101, 200],
    [201, 400],
    [401, 1000],
    [1001, 2000],
  ])("takes %s up to %s", (highest, top) => {
    expect(roundScaleTop(highest, { wholeNumbers: true })).toBe(top);
  });

  it("has a whole number at every quarter, from nothing up to 2000", () => {
    for (let highest = 0; highest <= 2_000; highest += 1) {
      const top = roundScaleTop(highest, { wholeNumbers: true });

      expect(top).toBeGreaterThanOrEqual(highest);
      for (const quarter of [1, 2, 3, 4]) {
        expect(Number.isInteger((top * quarter) / 4)).toBe(true);
      }
    }
  });
});
