import { describe, expect, it } from "vitest";

import { easeInOut, poseAt } from "./flight-path";

const FROM = { left: 100, top: 40, width: 40, height: 60 };
const TO = { left: 300, top: 400, width: 80 };

describe("the flight's easing", () => {
  it("starts and ends at rest and is slower at the ends than in the middle", () => {
    expect(easeInOut(0)).toBe(0);
    expect(easeInOut(1)).toBe(1);
    expect(easeInOut(0.1)).toBeLessThan(0.1);
    expect(easeInOut(0.9)).toBeGreaterThan(0.9);
    expect(easeInOut(0.5)).toBeCloseTo(0.5);
  });
});

describe("the flight's path", () => {
  it("starts exactly where the poster was, at its own size", () => {
    expect(poseAt(0, FROM, TO)).toEqual({ x: 100, y: 40, scale: 1 });
  });

  it("ends exactly on the shelf tile, scaled by the ratio of the two widths", () => {
    const pose = poseAt(1, FROM, TO);

    expect(pose.x).toBeCloseTo(300);
    expect(pose.y).toBeCloseTo(400);
    expect(pose.scale).toBeCloseTo(2);
  });

  it("follows a shelf tile that has moved since take-off", () => {
    const moved = { ...TO, left: TO.left + 120, top: TO.top - 30 };

    const pose = poseAt(1, FROM, moved);

    expect(pose.x).toBeCloseTo(420);
    expect(pose.y).toBeCloseTo(370);
  });

  it("rises a little on the way and is back to its own scale when it lands", () => {
    const halfway = poseAt(0.5, FROM, TO);
    const straight = 1 + (TO.width / FROM.width - 1) * 0.5;

    expect(halfway.scale).toBeGreaterThan(straight);
    expect(halfway.scale).toBeLessThan(straight * 1.05);
  });

  it("bows away from the straight line between the two places", () => {
    const halfway = poseAt(0.5, FROM, TO);
    const straightX = (FROM.left + TO.left) / 2;
    const straightY = (FROM.top + TO.top) / 2;

    expect(
      Math.hypot(halfway.x - straightX, halfway.y - straightY),
    ).toBeGreaterThan(10);
  });
});
