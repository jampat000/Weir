import { describe, expect, it } from "vitest";

import type { HandedBackBucket } from "../handed-back-model";
import {
  CHART_HEIGHT,
  CHART_WIDTH,
  chartShape,
  pointedAfterKey,
} from "./today-chart-model";

const bucket = (total: number): HandedBackBucket => ({
  from: 0,
  ok: total,
  same: 0,
  warn: 0,
  total,
});

describe("the line of the Today chart", () => {
  it("runs from the oldest bucket at the left edge to the newest at the right", () => {
    const { points } = chartShape([bucket(0), bucket(2), bucket(4)], 4);

    expect(points[0].x).toBe(0);
    expect(points[2].x).toBe(CHART_WIDTH);
  });

  it("puts the fullest bucket highest and an empty one near the floor", () => {
    const { points } = chartShape([bucket(0), bucket(4)], 4);

    expect(points[1].y).toBeLessThan(points[0].y);
    expect(points[0].y).toBeLessThan(CHART_HEIGHT);
  });

  it("closes the area down to the floor under the line", () => {
    const { area, line } = chartShape([bucket(1), bucket(2)], 2);

    expect(area.startsWith(line)).toBe(true);
    expect(area.endsWith("Z")).toBe(true);
  });

  it("draws a flat line when nothing finished", () => {
    const { points } = chartShape([bucket(0), bucket(0)], 1);

    expect(new Set(points.map((point) => point.y)).size).toBe(1);
  });

  it("draws nothing for no buckets", () => {
    expect(chartShape([], 1).area).toBe("");
  });
});

describe("walking the chart by keyboard", () => {
  it("steps by one and stops at the ends", () => {
    expect(pointedAfterKey("ArrowLeft", 3, 23)).toBe(2);
    expect(pointedAfterKey("ArrowLeft", 0, 23)).toBe(0);
    expect(pointedAfterKey("ArrowRight", 23, 23)).toBe(23);
  });

  it("jumps to the oldest and newest", () => {
    expect(pointedAfterKey("Home", 9, 23)).toBe(0);
    expect(pointedAfterKey("End", 9, 23)).toBe(23);
  });

  it("leaves other keys alone", () => {
    expect(pointedAfterKey("a", 9, 23)).toBeNull();
  });
});
