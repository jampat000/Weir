import { describe, expect, it } from "vitest";

import { COUNT_UP_MS, countUpValue, easeOutCubic } from "./count-up-math";

describe("the easing", () => {
  it("runs from 0 at the start to 1 at the end", () => {
    expect(easeOutCubic(0)).toBe(0);
    expect(easeOutCubic(1)).toBe(1);
  });

  it("is quick at first and settles gently", () => {
    expect(easeOutCubic(0.5)).toBeGreaterThan(0.5);
    expect(easeOutCubic(0.9) - easeOutCubic(0.8)).toBeLessThan(
      easeOutCubic(0.2) - easeOutCubic(0.1),
    );
  });

  it("stays between 0 and 1 for a time outside the count", () => {
    expect(easeOutCubic(-1)).toBe(0);
    expect(easeOutCubic(2)).toBe(1);
  });
});

describe("a count", () => {
  it("takes 520 ms", () => {
    expect(COUNT_UP_MS).toBe(520);
  });

  it("starts at the old value", () => {
    expect(countUpValue(10, 50, 0, COUNT_UP_MS)).toBe(10);
  });

  it("is between the two values part-way", () => {
    const part = countUpValue(10, 50, 200, COUNT_UP_MS);
    expect(part).toBeGreaterThan(10);
    expect(part).toBeLessThan(50);
  });

  it("is the new value once the time is up, exactly", () => {
    expect(countUpValue(10, 50, COUNT_UP_MS, COUNT_UP_MS)).toBe(50);
    expect(countUpValue(10, 50, 9999, COUNT_UP_MS)).toBe(50);
  });

  it("counts down as well as up", () => {
    expect(countUpValue(50, 10, 200, COUNT_UP_MS)).toBeLessThan(50);
    expect(countUpValue(50, 10, 200, COUNT_UP_MS)).toBeGreaterThan(10);
  });

  it("is the new value at once for a count of no length", () => {
    expect(countUpValue(10, 50, 0, 0)).toBe(50);
  });
});
