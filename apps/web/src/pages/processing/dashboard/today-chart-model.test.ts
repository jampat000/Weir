import { describe, expect, it } from "vitest";

import type { HandedBackBucket } from "../handed-back-model";
import {
  CHART_HEIGHT,
  CHART_WIDTH,
  chartShape,
  pointedAfterKey,
  pointedAtFraction,
  readoutLeft,
  scaleLines,
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

describe("the gridlines of the Today chart", () => {
  it("draws one at the top of the scale and one at half", () => {
    const [top, half] = scaleLines(10);

    expect(top.value).toBe(10);
    expect(half.value).toBe(5);
  });

  it("puts the top line where a bucket at the top of the scale is drawn", () => {
    const [top] = scaleLines(5);
    const { points } = chartShape([bucket(5)], 5);

    expect(top.y).toBe(points[0].y);
  });

  it("puts the half line midway between the top line and the line of an empty bucket", () => {
    const [top, half] = scaleLines(4);
    const { points } = chartShape([bucket(0)], 4);

    expect(half.y).toBeCloseTo((top.y + points[0].y) / 2);
  });
});

describe("pointing at the chart", () => {
  it("picks the bucket nearest the pointer, from the left edge to the right edge", () => {
    expect(pointedAtFraction(0, 23)).toBe(0);
    expect(pointedAtFraction(0.5, 23)).toBe(12);
    expect(pointedAtFraction(1, 23)).toBe(23);
  });

  it("stays on the chart when the pointer is beyond its edges", () => {
    expect(pointedAtFraction(-0.2, 23)).toBe(0);
    expect(pointedAtFraction(1.4, 23)).toBe(23);
  });
});

describe("where the pointer's readout sits", () => {
  it("is 10px right of the pointer's line, while there is room", () => {
    expect(readoutLeft(40, 120, 300)).toBe(50);
  });

  it("flips to the left of the line near the right edge", () => {
    expect(readoutLeft(280, 120, 300)).toBe(150);
  });

  it("stays 6px inside the chart's left edge", () => {
    expect(readoutLeft(2, 120, 130)).toBe(6);
  });

  it("flips to the left when the line is hard against the right edge", () => {
    expect(readoutLeft(300, 120, 300)).toBe(170);
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
